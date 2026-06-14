using Kuvox.Api.Modules.Auth.Contracts;
using Kuvox.Api.Modules.Auth.Dtos;
using Kuvox.Api.Modules.Auth.Enums;
using Kuvox.Api.Modules.Auth.Models;
using Kuvox.Api.Modules.Auth.Repositories;
using Kuvox.Api.Modules.Shared.Infrastructure;
using Kuvox.Api.Modules.Shared.Infrastructure.Email;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Kuvox.Api.Modules.Auth.Services;

/// <summary>
/// Real Auth business logic: registration, login, refresh-token rotation, logout, the
/// <c>/me</c> projection, email verification, and password reset. Persists via
/// <see cref="IUserRepository"/>, hashes passwords with <see cref="PasswordHasher{TUser}"/>,
/// issues tokens through <see cref="ITokenService"/>, sends transactional email via
/// <see cref="IEmailSender"/>, and publishes <see cref="UserRegisteredEvent"/> via MediatR (Rule 4).
/// </summary>
internal sealed class AuthService(
    IUserRepository users,
    ITokenService tokens,
    IPasswordHasher<User> passwordHasher,
    IMediator mediator,
    IEmailSender emailSender,
    IOptions<FrontendOptions> frontendOptions) : IAuthService
{
    private static readonly TimeSpan VerificationTokenLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan ResetTokenLifetime = TimeSpan.FromHours(1);

    private readonly string _frontendBaseUrl = frontendOptions.Value.BaseUrl.TrimEnd('/');

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

        // Send verification email (soft gate — signup succeeds regardless).
        await SendVerificationEmailAsync(user, cancellationToken);

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

        // Hard gate: unverified accounts cannot enter the app.
        if (user.EmailVerifiedAt is null)
        {
            throw AuthException.Forbidden("Please verify your email before signing in.");
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

    // ── Email verification ──────────────────────────────────────────────────────

    public async Task<VerifyEmailResult> VerifyEmailAsync(string token, CancellationToken cancellationToken = default)
    {
        var hash = tokens.HashToken(token);
        var stored = await users.GetActiveAuthTokenByHashAsync(hash, AuthTokenPurpose.EmailVerification, cancellationToken)
            ?? throw AuthException.BadRequest("Invalid or expired verification token.");

        var user = await users.GetByIdAsync(stored.UserId, cancellationToken)
            ?? throw AuthException.BadRequest("Invalid or expired verification token.");

        // "Brand-new account" proxy: was this click what flipped us from unverified to verified?
        var wasUnverified = user.EmailVerifiedAt is null;

        // Idempotent — if already verified, just consume the token.
        if (wasUnverified)
        {
            user.EmailVerifiedAt = DateTimeOffset.UtcNow;
        }

        stored.UsedAt = DateTimeOffset.UtcNow;
        await users.SaveChangesAsync(cancellationToken);

        // Auto-login: establish a session in the browser that opened the link.
        var issued = await IssueTokensAsync(user, cancellationToken);

        return new VerifyEmailResult(issued, wasUnverified);
    }

    public async Task ResendVerificationAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken)
            ?? throw AuthException.NotFound("User not found.");

        // No-op if already verified.
        if (user.EmailVerifiedAt is not null)
        {
            return;
        }

        // Invalidate any prior verification tokens so only the newest is valid.
        await users.InvalidateAuthTokensAsync(userId, AuthTokenPurpose.EmailVerification, cancellationToken);

        await SendVerificationEmailAsync(user, cancellationToken);
    }

    public async Task ResendVerificationByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        // Always succeed silently — no user enumeration. No-op if missing or already verified.
        var user = await users.GetByEmailAsync(email.Trim().ToLowerInvariant(), cancellationToken);
        if (user is null || user.EmailVerifiedAt is not null)
        {
            return;
        }

        // Invalidate any prior verification tokens so only the newest is valid.
        await users.InvalidateAuthTokensAsync(user.Id, AuthTokenPurpose.EmailVerification, cancellationToken);

        await SendVerificationEmailAsync(user, cancellationToken);
    }

    // ── Forgot / reset password ─────────────────────────────────────────────────

    public async Task ForgotPasswordAsync(string email, CancellationToken cancellationToken = default)
    {
        // Always succeed silently — no user enumeration.
        var user = await users.GetByEmailAsync(email.Trim().ToLowerInvariant(), cancellationToken);
        if (user is null)
        {
            return;
        }

        // Invalidate prior reset tokens so only the newest is valid.
        await users.InvalidateAuthTokensAsync(user.Id, AuthTokenPurpose.PasswordReset, cancellationToken);

        var (rawToken, tokenHash, expiresAt) = tokens.CreateSingleUseToken(ResetTokenLifetime);
        await users.AddAuthTokenAsync(new AuthToken
        {
            UserId = user.Id,
            Purpose = AuthTokenPurpose.PasswordReset,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
        }, cancellationToken);
        await users.SaveChangesAsync(cancellationToken);

        var resetLink = $"{_frontendBaseUrl}/reset-password?token={rawToken}";
        await emailSender.SendAsync(
            user.Email,
            "Reset your Kuvox password",
            $"""
            <p>Hi {user.DisplayName},</p>
            <p>Click the link below to reset your password (valid for 1 hour):</p>
            <p><a href="{resetLink}">{resetLink}</a></p>
            <p>If you didn't request this, you can ignore this email.</p>
            """,
            cancellationToken);
    }

    public async Task ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            throw AuthException.BadRequest("Password must be at least 8 characters.");
        }

        var hash = tokens.HashToken(token);
        var stored = await users.GetActiveAuthTokenByHashAsync(hash, AuthTokenPurpose.PasswordReset, cancellationToken)
            ?? throw AuthException.BadRequest("Invalid or expired reset token.");

        var user = await users.GetByIdAsync(stored.UserId, cancellationToken)
            ?? throw AuthException.BadRequest("Invalid or expired reset token.");

        user.PasswordHash = passwordHasher.HashPassword(user, newPassword);
        stored.UsedAt = DateTimeOffset.UtcNow;

        await users.SaveChangesAsync(cancellationToken);

        // Force re-login everywhere by revoking all existing refresh tokens.
        await users.RevokeAllRefreshTokensAsync(user.Id, cancellationToken);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private async Task SendVerificationEmailAsync(User user, CancellationToken cancellationToken)
    {
        var (rawToken, tokenHash, expiresAt) = tokens.CreateSingleUseToken(VerificationTokenLifetime);
        await users.AddAuthTokenAsync(new AuthToken
        {
            UserId = user.Id,
            Purpose = AuthTokenPurpose.EmailVerification,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
        }, cancellationToken);
        await users.SaveChangesAsync(cancellationToken);

        var verifyLink = $"{_frontendBaseUrl}/verify-email?token={rawToken}";
        await emailSender.SendAsync(
            user.Email,
            "Verify your Kuvox email",
            $"""
            <p>Hi {user.DisplayName},</p>
            <p>Thanks for signing up! Click the link below to verify your email (valid for 24 hours):</p>
            <p><a href="{verifyLink}">{verifyLink}</a></p>
            <p>If you didn't create a Kuvox account, you can ignore this email.</p>
            """,
            cancellationToken);
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
        new(user.Id, user.Email, user.DisplayName, "user", user.Plan.ToString(), user.EmailVerifiedAt is not null, user.CreatedAt);
}
