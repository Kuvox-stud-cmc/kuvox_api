using Kuvox.Api.Modules.Timelines.Dtos;
using Kuvox.Api.Modules.Timelines.Services;
using Kuvox.Api.Modules.Shared.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kuvox.Api.Modules.Timelines.Controllers;

/// <summary>
/// Real Timelines endpoints for video editor document sync and render job creation.
/// </summary>
[Authorize]
[ApiController]
[Route("api/timelines")]
[Produces("application/json")]
public sealed class TimelinesController(ITimelineService timelines) : ControllerBase
{
    [HttpGet("projects/{projectId:guid}/current")]
    public Task<TimelineDocumentDto> GetCurrentDocument(Guid projectId, CancellationToken ct) =>
        timelines.GetCurrentDocumentAsync(projectId, Caller(), ct);

    [HttpPut("projects/{projectId:guid}/current")]
    public Task<TimelineDocumentDto> SaveCurrentDocument(Guid projectId, SaveTimelineDocumentRequest request, CancellationToken ct) =>
        timelines.SaveCurrentDocumentAsync(projectId, Caller(), request, ct);

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
    public Task<RenderJobDto> RequestRender(Guid id, RenderTimelineRequest request, CancellationToken ct) =>
        timelines.RequestRenderAsync(id, Caller(), request, ct);

    [HttpGet("render-jobs/{jobId:guid}")]
    public Task<RenderJobDto> GetRenderJob(Guid jobId, CancellationToken ct) =>
        timelines.GetRenderJobAsync(jobId, Caller(), ct);

    [HttpPost("projects/{projectId:guid}/performance")]
    public async Task<IActionResult> RecordPerformance(Guid projectId, RecordVideoEditorPerformanceRequest request, CancellationToken ct)
    {
        await timelines.RecordPerformanceAsync(projectId, Caller(), request, ct);
        return NoContent();
    }

    private CallerContext Caller() =>
        User.ToCallerContext() ?? throw DomainException.Forbidden("Invalid token.");
}
