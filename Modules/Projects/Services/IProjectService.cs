using Kuvox.Api.Modules.Projects.Dtos;
using Kuvox.Api.Modules.Shared.Dtos;

namespace Kuvox.Api.Modules.Projects.Services;

/// <summary>
/// Module-internal business API of the Projects module (scaffolded, not yet implemented).
/// Public only for the public controller's DI; impl stays <c>internal</c> (Rule 1). The
/// cross-module surface is <c>Projects.Contracts</c> (Rule 2).
/// </summary>
public interface IProjectService
{
    Task<PagedResult<ProjectDto>> ListAsync(Guid ownerId, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<ProjectDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ProjectDto> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken = default);

    Task<ProjectDto> UpdateAsync(Guid id, UpdateProjectRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
