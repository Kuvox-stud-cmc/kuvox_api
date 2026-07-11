using System.IdentityModel.Tokens.Jwt;
using Kuvox.Api.Modules.Auth.Dtos;
using Kuvox.Api.Modules.Auth.Enums;
using Kuvox.Api.Modules.Auth.Models;
using Kuvox.Api.Modules.Auth.Repositories;
using Kuvox.Api.Modules.Auth.Services;
using Kuvox.Api.Modules.Shared.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tests;

public sealed class AuthServiceSessionTests
{
    private const string Email = "user@example.com";
    private const string Password = "Password123!";

    [Fact]
    public async Task LoginAsync_blocks_second_login_while_session_is_active()
    {
        var (service, repo, user) = CreateService();

        var first = await service.LoginAsync(new LoginRequest(Email, Password));
        var firstSessionId = user.ActiveSessionId;
        Assert.NotNull(firstSessionId);
        Assert.Equal(firstSessionId.ToString(), ReadSessionId(first.AccessToken));

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            service.LoginAsync(new LoginRequest(Email, Password)));

        Assert.Equal(StatusCodes.Status409Conflict, ex.StatusCode);
        Assert.Equal("active_session_conflict", ex.Code);
        Assert.Single(repo.RefreshTokens, t => t.IsActive);
        Assert.Equal(firstSessionId, user.ActiveSessionId);
    }

    [Fact]
    public async Task LoginAsync_takeover_revokes_old_session_and_creates_one_new_session()
    {
        var (service, repo, user) = CreateService();
        var first = await service.LoginAsync(new LoginRequest(Email, Password));
        var firstSessionId = user.ActiveSessionId!.Value;

        var second = await service.LoginAsync(new LoginRequest(Email, Password, ReplaceExistingSession: true));
        var secondSessionId = user.ActiveSessionId!.Value;

        Assert.NotEqual(firstSessionId, secondSessionId);
        Assert.Equal(secondSessionId.ToString(), ReadSessionId(second.AccessToken));
        Assert.Single(repo.RefreshTokens, t => t.IsActive);
        Assert.All(
            repo.RefreshTokens.Where(t => t.SessionId == firstSessionId),
            token => Assert.NotNull(token.RevokedAt));
        Assert.False(await repo.IsActiveSessionAsync(user.Id, firstSessionId));

        var refreshError = await Assert.ThrowsAsync<AuthException>(() => service.RefreshAsync(first.RefreshToken));
        Assert.Equal(StatusCodes.Status401Unauthorized, refreshError.StatusCode);
        Assert.Equal("session_replaced", refreshError.Code);
    }

    [Fact]
    public async Task LoginAsync_takeover_with_incorrect_password_keeps_existing_session()
    {
        var (service, repo, user) = CreateService();
        await service.LoginAsync(new LoginRequest(Email, Password));
        var firstSessionId = user.ActiveSessionId;

        var error = await Assert.ThrowsAsync<AuthException>(() =>
            service.LoginAsync(new LoginRequest(Email, "incorrect", ReplaceExistingSession: true)));

        Assert.Equal(StatusCodes.Status401Unauthorized, error.StatusCode);
        Assert.Equal(firstSessionId, user.ActiveSessionId);
        Assert.Single(repo.RefreshTokens, t => t.IsActive);
    }

    [Fact]
    public async Task LoginAsync_allows_normal_login_when_old_refresh_token_expired()
    {
        var (service, repo, user) = CreateService();
        await service.LoginAsync(new LoginRequest(Email, Password));
        var firstSessionId = user.ActiveSessionId;
        repo.RefreshTokens.Single().ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);

        var second = await service.LoginAsync(new LoginRequest(Email, Password));

        Assert.NotEqual(firstSessionId, user.ActiveSessionId);
        Assert.Equal(user.ActiveSessionId.ToString(), ReadSessionId(second.AccessToken));
        Assert.Single(repo.RefreshTokens, t => t.IsActive);
    }

    [Fact]
    public async Task Concurrent_normal_login_and_takeover_leave_one_active_session()
    {
        var (service, repo, user) = CreateService();
        await service.LoginAsync(new LoginRequest(Email, Password));

        var normalLogin = CaptureLoginAsync(service, replaceExistingSession: false);
        var takeover = CaptureLoginAsync(service, replaceExistingSession: true);
        var results = await Task.WhenAll(normalLogin, takeover);

        Assert.Contains(results, result => result.Tokens is not null);
        Assert.Contains(results, result => result.Error?.Code == "active_session_conflict");
        Assert.Single(repo.RefreshTokens, t => t.IsActive);
        Assert.NotNull(user.ActiveSessionId);
    }

    [Fact]
    public async Task LogoutAsync_clears_active_session_and_allows_new_login()
    {
        var (service, repo, user) = CreateService();

        var first = await service.LoginAsync(new LoginRequest(Email, Password));
        var firstSessionId = user.ActiveSessionId!.Value;

        await service.LogoutAsync(first.RefreshToken);

        Assert.Null(user.ActiveSessionId);
        Assert.False(await repo.IsActiveSessionAsync(user.Id, firstSessionId));

        var second = await service.LoginAsync(new LoginRequest(Email, Password));

        Assert.NotNull(user.ActiveSessionId);
        Assert.NotEqual(firstSessionId.ToString(), ReadSessionId(second.AccessToken));
    }

    [Fact]
    public async Task RefreshAsync_rotates_token_inside_current_session()
    {
        var (service, repo, user) = CreateService();

        var issued = await service.LoginAsync(new LoginRequest(Email, Password));
        var sessionId = user.ActiveSessionId!.Value;

        var refreshed = await service.RefreshAsync(issued.RefreshToken);

        Assert.Equal(sessionId.ToString(), ReadSessionId(refreshed.AccessToken));
        Assert.Single(repo.RefreshTokens, t => t.IsActive);
        Assert.Single(repo.RefreshTokens, t => t.RevokedAt is not null);

        var ex = await Assert.ThrowsAsync<AuthException>(() => service.RefreshAsync(issued.RefreshToken));
        Assert.Equal(StatusCodes.Status401Unauthorized, ex.StatusCode);
    }

    [Fact]
    public async Task ChangePasswordAsync_revokes_active_session()
    {
        var (service, repo, user) = CreateService();
        await service.LoginAsync(new LoginRequest(Email, Password));
        var sessionId = user.ActiveSessionId!.Value;

        await service.ChangePasswordAsync(
            user.Id,
            new ChangePasswordRequest(Password, "NewPassword123!"));

        Assert.Null(user.ActiveSessionId);
        Assert.False(await repo.IsActiveSessionAsync(user.Id, sessionId));
        Assert.DoesNotContain(repo.RefreshTokens, t => t.IsActive);
    }

    private static (AuthService Service, FakeUserRepository Repository, User User) CreateService()
    {
        var repo = new FakeUserRepository();
        var hasher = new PasswordHasher<User>();
        var user = new User
        {
            Email = Email,
            DisplayName = "Test User",
            PasswordHash = string.Empty,
            EmailVerifiedAt = DateTimeOffset.UtcNow,
        };
        user.PasswordHash = hasher.HashPassword(user, Password);
        repo.Users.Add(user);

        var tokenService = new TokenService(Options.Create(new JwtOptions
        {
            Secret = new string('s', 64),
            Issuer = "test",
            Audience = "test",
            AccessTokenMinutes = 15,
            RefreshTokenDays = 7,
        }));

        var service = new AuthService(
            repo,
            tokenService,
            studioService: null!,
            hasher,
            mediator: null!,
            emailSender: null!,
            Options.Create(new FrontendOptions { BaseUrl = "http://localhost:5173" }));

        return (service, repo, user);
    }

    private static string? ReadSessionId(string accessToken) =>
        new JwtSecurityTokenHandler()
            .ReadJwtToken(accessToken)
            .Claims
            .FirstOrDefault(c => c.Type == TokenService.SessionClaimType)
            ?.Value;

    private static async Task<(AuthTokenDto? Tokens, AuthException? Error)> CaptureLoginAsync(
        AuthService service,
        bool replaceExistingSession)
    {
        try
        {
            return (
                await service.LoginAsync(new LoginRequest(Email, Password, replaceExistingSession)),
                null);
        }
        catch (AuthException error)
        {
            return (null, error);
        }
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        private readonly object _gate = new();

        public List<User> Users { get; } = [];

        public List<RefreshToken> RefreshTokens { get; } = [];

        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Users.Any(u => u.Id == id));

        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Users.FirstOrDefault(u => u.Id == id));

        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult(Users.FirstOrDefault(u => u.Email == email));

        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult(Users.Any(u => u.Email == email));

        public Task AddAsync(User user, CancellationToken cancellationToken = default)
        {
            Users.Add(user);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<(Guid StudioId, UserStudioRole Role)>> GetStudioMembershipsAsync(
            Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<(Guid StudioId, UserStudioRole Role)>>([]);

        public Task AddRefreshTokenAsync(RefreshToken token, CancellationToken cancellationToken = default)
        {
            RefreshTokens.Add(token);
            return Task.CompletedTask;
        }

        public Task<RefreshToken?> GetRefreshTokenByHashAsync(
            string tokenHash, CancellationToken cancellationToken = default) =>
            Task.FromResult(RefreshTokens.FirstOrDefault(rt => rt.TokenHash == tokenHash));

        public Task<CreateSessionResult> CreateSessionAsync(
            Guid userId,
            Guid sessionId,
            RefreshToken refreshToken,
            bool replaceExistingSession,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var user = Users.FirstOrDefault(u => u.Id == userId);
                if (user is null)
                {
                    return Task.FromResult(CreateSessionResult.UserNotFound);
                }

                if (user.ActiveSessionId is { } activeSessionId)
                {
                    var activeTokenExists = RefreshTokens.Any(
                        rt => rt.UserId == userId && rt.SessionId == activeSessionId && rt.IsActive);

                    if (activeTokenExists && !replaceExistingSession)
                    {
                        return Task.FromResult(CreateSessionResult.ActiveSessionConflict);
                    }

                    if (replaceExistingSession)
                    {
                        foreach (var token in RefreshTokens.Where(
                            rt => rt.UserId == userId && rt.SessionId == activeSessionId && rt.RevokedAt is null))
                        {
                            token.RevokedAt = DateTimeOffset.UtcNow;
                        }
                    }
                }

                user.ActiveSessionId = sessionId;
                refreshToken.UserId = userId;
                refreshToken.SessionId = sessionId;
                RefreshTokens.Add(refreshToken);
                return Task.FromResult(CreateSessionResult.Created);
            }
        }

        public Task<bool> TryRotateRefreshTokenAsync(
            Guid refreshTokenId,
            Guid sessionId,
            string replacementTokenHash,
            RefreshToken replacementToken,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var current = RefreshTokens.FirstOrDefault(rt => rt.Id == refreshTokenId);
                if (current is null || current.SessionId != sessionId || !current.IsActive)
                {
                    return Task.FromResult(false);
                }

                var user = Users.FirstOrDefault(u => u.Id == current.UserId);
                if (user?.ActiveSessionId != sessionId)
                {
                    return Task.FromResult(false);
                }

                current.RevokedAt = DateTimeOffset.UtcNow;
                current.ReplacedByTokenHash = replacementTokenHash;
                replacementToken.UserId = current.UserId;
                replacementToken.SessionId = sessionId;
                RefreshTokens.Add(replacementToken);
                return Task.FromResult(true);
            }
        }

        public Task<bool> IsActiveSessionAsync(
            Guid userId, Guid sessionId, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var isActive = Users.Any(u => u.Id == userId && u.ActiveSessionId == sessionId)
                    && RefreshTokens.Any(rt => rt.UserId == userId && rt.SessionId == sessionId && rt.IsActive);

                return Task.FromResult(isActive);
            }
        }

        public Task RevokeSessionAsync(
            Guid userId, Guid sessionId, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                foreach (var token in RefreshTokens.Where(
                    rt => rt.UserId == userId && rt.SessionId == sessionId && rt.RevokedAt is null))
                {
                    token.RevokedAt = DateTimeOffset.UtcNow;
                }

                var user = Users.FirstOrDefault(u => u.Id == userId && u.ActiveSessionId == sessionId);
                if (user is not null)
                {
                    user.ActiveSessionId = null;
                }
            }

            return Task.CompletedTask;
        }

        public Task RevokeAllRefreshTokensAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                foreach (var token in RefreshTokens.Where(rt => rt.UserId == userId && rt.RevokedAt is null))
                {
                    token.RevokedAt = DateTimeOffset.UtcNow;
                }

                var user = Users.FirstOrDefault(u => u.Id == userId);
                if (user is not null)
                {
                    user.ActiveSessionId = null;
                }
            }

            return Task.CompletedTask;
        }

        public Task AddAuthTokenAsync(AuthToken token, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AuthToken?> GetActiveAuthTokenByHashAsync(
            string tokenHash, AuthTokenPurpose purpose, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task InvalidateAuthTokensAsync(
            Guid userId, AuthTokenPurpose purpose, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
