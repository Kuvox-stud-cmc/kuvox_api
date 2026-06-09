using Kuvox.Api.Modules.Auth.Contracts;
using Kuvox.Api.Modules.Auth.Repositories;

namespace Kuvox.Api.Modules.Auth.Services;

/// <summary>
/// Implements the public <see cref="IAuthApi"/> (Rule 2) — the read-only facade other
/// modules use. Kept thin and functional (unlike <see cref="AuthService"/>) so cross-module
/// lookups work today; it only exposes the shareable <see cref="UserSummary"/> projection.
/// </summary>
internal sealed class AuthApi(IUserRepository users) : IAuthApi
{
    public Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        users.ExistsAsync(userId, cancellationToken);

    public async Task<UserSummary?> GetSummaryAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        return user is null ? null : new UserSummary(user.Id, user.Email, user.DisplayName);
    }

    public async Task<UserSummary?> GetSummaryByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByEmailAsync(email.Trim().ToLowerInvariant(), cancellationToken);
        return user is null ? null : new UserSummary(user.Id, user.Email, user.DisplayName);
    }
}
