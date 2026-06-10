using Kuvox.Api.Modules.Auth.Enums;
using Kuvox.Api.Modules.Auth.Models;

namespace Kuvox.Api.Modules.Auth.Repositories;

/// <summary>Persistence boundary for studios + memberships. Internal to the Auth module.</summary>
internal interface IStudioRepository
{
    Task<Studio?> GetByIdAsync(Guid studioId, CancellationToken cancellationToken = default);

    /// <summary>Studios the user belongs to, with their role in each.</summary>
    Task<IReadOnlyList<(Studio Studio, UserStudioRole Role)>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<UserStudio?> GetMembershipAsync(Guid studioId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Members of a studio joined with their user record (for display).</summary>
    Task<IReadOnlyList<(User User, UserStudioRole Role)>> ListMembersAsync(Guid studioId, CancellationToken cancellationToken = default);

    Task<int> CountAdminsAsync(Guid studioId, CancellationToken cancellationToken = default);

    Task AddStudioAsync(Studio studio, CancellationToken cancellationToken = default);

    Task AddMembershipAsync(UserStudio membership, CancellationToken cancellationToken = default);

    void RemoveMembership(UserStudio membership);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    void RemoveStudio(Studio studio);
}
