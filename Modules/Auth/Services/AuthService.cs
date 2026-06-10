using Kuvox.Api.Modules.Auth.Contracts;
using Kuvox.Api.Modules.Auth.Dtos;
using Kuvox.Api.Modules.Auth.Models;
using Kuvox.Api.Modules.Auth.Repositories;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace Kuvox.Api.Modules.Auth.Services;

/// <summary>
/// Real Auth business logic: registration, login, refresh-token rotation, logout, and the
/// <c>/me</c> projection. Persists via <see cref="IUserRepository"/>, hashes passwords with
/// <see cref="PasswordHasher{TUser}"/>, issues tokens through <see cref="ITokenService"/>,
/// and publishes <see cref="UserRegisteredEvent"/> via MediatR (Rule 4).
/// </summary>
internal sealed class AuthService(
    IUserRepository users,
    ITokenService tokens,
    IPasswordHasher<User> passwordHasher,
    IMediator mediator) : IAuthService
{
    public async Task<UserDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        if (await users.EmailExistsAsync(email, cancellationToken))
        {
            throw AuthException.Conflict("An account with this email already exists.");
        }

        var user = new User
        {
            Email = email,
            DisplayName = request.DisplayName.Trim(),
            PasswordHash = string.Empty,
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        await users.AddAsync(user, cancellationToken);
        await users.SaveChangesAsync(cancellationToken);

        await mediator.Publish(new UserRegisteredEvent(user.Id, user.Email), cancellationToken);

        return ToDto(user);
    }

    public async Task<AuthTokenDto> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await users.GetByEmailAsync(email, cancellationToken);

        if (user is null)
        {
            throw AuthException.Unauthorized("Invalid email or password.");
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            throw AuthException.Unauthorized("Invalid email or password.");
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
            await users.SaveChangesAsync(cancellationToken);
        }

        return await IssueTokensAsync(user, cancellationToken);
    }

    public async Task<AuthTokenDto> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var hash = tokens.HashToken(refreshToken);
        var stored = await users.GetRefreshTokenByHashAsync(hash, cancellationToken);

        if (stored is null || !stored.IsActive)
        {
            throw AuthException.Unauthorized("Invalid or expired refresh token.");
        }

        var user = await users.GetByIdAsync(stored.UserId, cancellationToken)
            ?? throw AuthException.Unauthorized("Invalid or expired refresh token.");

        // Rotate: revoke the presented token and issue a fresh pair.
        var result = await IssueTokensAsync(user, cancellationToken, beforeSave: newHash =>
        {
            stored.RevokedAt = DateTimeOffset.UtcNow;
            stored.ReplacedByTokenHash = newHash;
        });

        return result;
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var hash = tokens.HashToken(refreshToken);
        var stored = await users.GetRefreshTokenByHashAsync(hash, cancellationToken);

        if (stored is { RevokedAt: null })
        {
            stored.RevokedAt = DateTimeOffset.UtcNow;
            await users.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<UserDto?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        return user is null ? null : ToDto(user);
    }

    private async Task<AuthTokenDto> IssueTokensAsync(
        User user, CancellationToken cancellationToken, Action<string>? beforeSave = null)
    {
        var memberships = await users.GetStudioMembershipsAsync(user.Id, cancellationToken);
        var (accessToken, expiresAt) = tokens.CreateAccessToken(user, memberships);
        var (refreshToken, refreshHash, refreshExpiresAt) = tokens.CreateRefreshToken();

        beforeSave?.Invoke(refreshHash);

        await users.AddRefreshTokenAsync(
            new RefreshToken
            {
                UserId = user.Id,
                TokenHash = refreshHash,
                ExpiresAt = refreshExpiresAt,
            },
            cancellationToken);
        await users.SaveChangesAsync(cancellationToken);

        return new AuthTokenDto(accessToken, refreshToken, expiresAt);
    }

    private static UserDto ToDto(User user) =>
        new(user.Id, user.Email, user.DisplayName, "user", user.Plan.ToString(), user.CreatedAt);
}
