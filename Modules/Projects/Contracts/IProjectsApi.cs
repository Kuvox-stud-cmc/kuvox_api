namespace Kuvox.Api.Modules.Projects.Contracts;

using Kuvox.Api.Modules.Shared.Infrastructure;

/// <summary>Public cross-module API of the Projects module (Rule 2).</summary>
public interface IProjectsApi
{
    Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<ProjectSummary?> GetSummaryAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<ProjectDocumentAccess> RequireReadAccessAsync(Guid projectId, CallerContext caller, CancellationToken cancellationToken = default);

    Task<ProjectDocumentAccess> RequireWriteAccessAsync(Guid projectId, CallerContext caller, CancellationToken cancellationToken = default);

    Task<int> CountByWorkspaceAsync(Guid ownerId, ProjectOwnerKind ownerKind, CancellationToken cancellationToken = default);
}
