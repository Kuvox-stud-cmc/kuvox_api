using Kuvox.Api.Modules.Projects.Dtos;
using Kuvox.Api.Modules.Shared.Dtos;
using Kuvox.Api.Modules.Shared.Infrastructure;

namespace Kuvox.Api.Modules.Projects.Services;

/// <summary>
/// Module-internal business API of the Projects module. Public only for the public
/// controller's DI; impl stays <c>internal</c> (Rule 1). The cross-module surface is
/// <c>Projects.Contracts</c> (Rule 2). Workspace scoping and per-resource authorization are
/// driven by the <see cref="WorkspaceScope"/> / <see cref="CallerContext"/> the controller
/// resolves from the JWT.
/// </summary>
public interface IProjectService
{
    Task<PagedResult<ProjectDto>> ListByWorkspaceAsync(WorkspaceScope scope, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<PagedResult<ProjectDto>> ListSharedWithMeAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<PagedResult<ProjectTrashItemDto>> ListTrashAsync(WorkspaceScope scope, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<ProjectDto> GetAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default);

    Task<ProjectDto> CreateAsync(WorkspaceScope scope, CallerContext caller, CreateProjectRequest request, CancellationToken cancellationToken = default);

    Task<ProjectDto> UpdateAsync(Guid id, CallerContext caller, UpdateProjectRequest request, CancellationToken cancellationToken = default);

    Task ShareAsync(Guid id, CallerContext caller, ShareProjectRequest request, CancellationToken cancellationToken = default);

    Task UnshareAsync(Guid id, CallerContext caller, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Moves a project to Trash (soft-delete).</summary>
    Task SoftDeleteAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default);

    Task RestoreAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default);

    /// <summary>Hard-deletes a trashed project and fires <c>ProjectDeletedEvent</c> for cleanup.</summary>
    Task PermanentDeleteAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default);
}
