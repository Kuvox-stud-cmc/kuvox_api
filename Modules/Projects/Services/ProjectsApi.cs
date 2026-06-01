using Kuvox.Api.Modules.Projects.Contracts;
using Kuvox.Api.Modules.Projects.Repositories;

namespace Kuvox.Api.Modules.Projects.Services;

/// <summary>Implements the public <see cref="IProjectsApi"/> read facade (Rule 2). Internal (Rule 1).</summary>
internal sealed class ProjectsApi(IProjectRepository projects) : IProjectsApi
{
    public Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        projects.ExistsAsync(projectId, cancellationToken);

    public async Task<ProjectSummary?> GetSummaryAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await projects.GetByIdAsync(projectId, cancellationToken);
        return project is null
            ? null
            : new ProjectSummary(project.Id, project.OwnerId, project.Name, project.Status);
    }
}
