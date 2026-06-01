using Kuvox.Api.Modules.Projects.Dtos;
using Kuvox.Api.Modules.Projects.Services;
using Kuvox.Api.Modules.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Kuvox.Api.Modules.Projects.Controllers;

/// <summary>
/// Real Projects endpoints, backed by the not-yet-implemented <see cref="IProjectService"/>
/// (returns <c>501</c>). Use <c>/api/mock/projects</c> for working fake data.
/// </summary>
[ApiController]
[Route("api/projects")]
[Produces("application/json")]
public sealed class ProjectsController(IProjectService projects) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<ProjectDto>> List([FromQuery] Guid ownerId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        projects.ListAsync(ownerId, page, pageSize, ct);

    [HttpGet("{id:guid}")]
    public Task<ProjectDto?> Get(Guid id, CancellationToken ct) => projects.GetAsync(id, ct);

    [HttpPost]
    public Task<ProjectDto> Create(CreateProjectRequest request, CancellationToken ct) =>
        projects.CreateAsync(request, ct);

    [HttpPut("{id:guid}")]
    public Task<ProjectDto> Update(Guid id, UpdateProjectRequest request, CancellationToken ct) =>
        projects.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    public Task Delete(Guid id, CancellationToken ct) => projects.DeleteAsync(id, ct);
}
