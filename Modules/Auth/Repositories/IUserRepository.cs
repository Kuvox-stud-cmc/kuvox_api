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

    /// <summary>Revokes every active refresh token for a user (used after a password reset).</summary>
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
