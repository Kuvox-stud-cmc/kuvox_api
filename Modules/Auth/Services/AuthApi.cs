using Kuvox.Api.Modules.Auth.Contracts;
using Kuvox.Api.Modules.Auth.Enums;
using Kuvox.Api.Modules.Auth.Repositories;

namespace Kuvox.Api.Modules.Auth.Services;

/// <summary>
/// Implements the public <see cref="IAuthApi"/> (Rule 2) — the read-only facade other
/// modules use. Kept thin and functional (unlike <see cref="AuthService"/>) so cross-module
/// lookups work today; it only exposes the shareable <see cref="UserSummary"/> projection.
/// </summary>
internal sealed class AuthApi(IUserRepository users, IStudioRepository studios) : IAuthApi
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

    public async Task<UserPlanLimits?> GetPlanLimitsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        return user is null
            ? null
            : new UserPlanLimits(user.Plan.ToString(), StorageBytesFor(user.Plan));
    }

    public async Task<IReadOnlyList<StudioMemberSummary>> ListStudioMembersAsync(
        Guid studioId,
        CancellationToken cancellationToken = default)
    {
        var members = await studios.ListMembersAsync(studioId, cancellationToken);
        return members
            .Select(member => new StudioMemberSummary(
                member.User.Id,
                member.User.Email,
                member.User.DisplayName,
                member.Role.ToString()))
            .ToList();
    }

    public async Task<StudioMemberSummary?> GetStudioMemberAsync(
        Guid studioId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var members = await ListStudioMembersAsync(studioId, cancellationToken);
        return members.FirstOrDefault(member => member.UserId == userId);
    }

    private static long StorageBytesFor(UserPlan plan) =>
        plan switch
        {
            UserPlan.Creator => 100L * 1024L * 1024L * 1024L,
            _ => 5L * 1024L * 1024L * 1024L,
        };
}
