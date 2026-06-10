using Kuvox.Api.Modules.Projects.Enums;
using Kuvox.Api.Modules.Projects.Models;

namespace Kuvox.Api.Modules.Projects.Repositories;

internal interface IProjectRepository
{
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Live (non-deleted) projects owned by a workspace, newest-updated first, paged.</summary>
    Task<(IReadOnlyList<Project> Items, int Total)> ListByWorkspaceAsync(
        OwnerKind ownerKind, Guid ownerId, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Live projects shared with a user via <c>project_users</c> (excludes ones they own), paged.</summary>
    Task<(IReadOnlyList<(Project Project, ProjectRole Role)> Items, int Total)> ListSharedWithUserAsync(
        Guid userId, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Trashed (soft-deleted) projects owned by a workspace, newest-deleted first, paged.</summary>
    Task<(IReadOnlyList<Project> Items, int Total)> ListTrashAsync(
        OwnerKind ownerKind, Guid ownerId, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Projects soft-deleted before <paramref name="cutoff"/> (for auto-purge).</summary>
    Task<IReadOnlyList<Project>> ListDeletedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);

    Task<ProjectUser?> GetProjectUserAsync(Guid projectId, Guid userId, CancellationToken cancellationToken = default);

    Task AddAsync(Project project, CancellationToken cancellationToken = default);

    Task AddProjectUserAsync(ProjectUser projectUser, CancellationToken cancellationToken = default);

    void RemoveProjectUser(ProjectUser projectUser);

    void Remove(Project project);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
