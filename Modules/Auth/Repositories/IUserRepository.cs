using Kuvox.Api.Modules.Auth.Enums;
using Kuvox.Api.Modules.Auth.Models;

namespace Kuvox.Api.Modules.Auth.Repositories;

/// <summary>Persistence boundary for <see cref="User"/>. Internal to the Auth module.</summary>
internal interface IUserRepository
{
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);

    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default);

    Task AddAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>Studio memberships for a user, used to build <c>studio</c> JWT claims.</summary>
    Task<IReadOnlyList<(Guid StudioId, UserStudioRole Role)>> GetStudioMembershipsAsync(
        Guid userId, CancellationToken cancellationToken = default);

    Task AddRefreshTokenAsync(RefreshToken token, CancellationToken cancellationToken = default);

    Task<RefreshToken?> GetRefreshTokenByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Creates the account's active session and first refresh token if no other session is active.</summary>
    Task<bool> TryCreateSessionAsync(
        Guid userId, Guid sessionId, RefreshToken refreshToken, CancellationToken cancellationToken = default);

    /// <summary>Rotates an active refresh token inside the current server-side session.</summary>
    Task<bool> TryRotateRefreshTokenAsync(
        Guid refreshTokenId,
        Guid sessionId,
        string replacementTokenHash,
        RefreshToken replacementToken,
        CancellationToken cancellationToken = default);

    /// <summary>True when the user still owns this session and it has an active refresh token.</summary>
    Task<bool> IsActiveSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Revokes every active refresh token for a session and clears it if it is current.</summary>
    Task RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Revokes every active refresh token for a user and clears their active session.</summary>
    Task RevokeAllRefreshTokensAsync(Guid userId, CancellationToken cancellationToken = default);

    Task AddAuthTokenAsync(AuthToken token, CancellationToken cancellationToken = default);

    /// <summary>The active (unused, unexpired) token matching a hash + purpose, if any.</summary>
    Task<AuthToken?> GetActiveAuthTokenByHashAsync(
        string tokenHash, AuthTokenPurpose purpose, CancellationToken cancellationToken = default);

    /// <summary>Marks any prior unused tokens of a purpose as used, so only the newest is valid.</summary>
    Task InvalidateAuthTokensAsync(
        Guid userId, AuthTokenPurpose purpose, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
