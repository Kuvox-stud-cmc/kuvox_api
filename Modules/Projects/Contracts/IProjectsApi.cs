namespace Kuvox.Api.Modules.Projects.Contracts;

/// <summary>Public cross-module API of the Projects module (Rule 2).</summary>
public interface IProjectsApi
{
    Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<ProjectSummary?> GetSummaryAsync(Guid projectId, CancellationToken cancellationToken = default);
}
