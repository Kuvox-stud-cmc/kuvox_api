using Kuvox.Api.Modules.Timelines.Dtos;
using Kuvox.Api.Modules.Timelines.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kuvox.Api.Modules.Timelines.Controllers;

/// <summary>
/// Real Timelines endpoints, backed by the not-yet-implemented <see cref="ITimelineService"/>
/// (returns <c>501</c>). Use <c>/api/mock/timelines</c> for working fake data.
/// </summary>
[Authorize]
[ApiController]
[Route("api/timelines")]
[Produces("application/json")]
public sealed class TimelinesController(ITimelineService timelines) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<TimelineDto>> ListByProject([FromQuery] Guid projectId, CancellationToken ct) =>
        timelines.ListByProjectAsync(projectId, ct);

    [HttpPost]
    public Task<TimelineDto> Create(CreateTimelineRequest request, CancellationToken ct) =>
        timelines.CreateAsync(request, ct);

    [HttpPost("{id:guid}/revisions")]
    public Task<TimelineRevisionDto> AddRevision(Guid id, CreateRevisionRequest request, CancellationToken ct) =>
        timelines.AddRevisionAsync(id, request, ct);

    [HttpPost("{id:guid}/render")]
    public Task<RenderJobDto> RequestRender(Guid id, CancellationToken ct) =>
        timelines.RequestRenderAsync(id, ct);
}
