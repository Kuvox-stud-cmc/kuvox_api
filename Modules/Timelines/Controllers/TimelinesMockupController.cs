using Kuvox.Api.Modules.Timelines.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Kuvox.Api.Modules.Timelines.Controllers;

/// <summary>Mockup Timelines endpoints returning canned data (no DB).</summary>
[ApiController]
[Route("api/mock/timelines")]
[Produces("application/json")]
public sealed class TimelinesMockupController : ControllerBase
{
    private static readonly Guid SampleProject = Guid.Parse("0192a400-0000-7000-8000-000000000001");
    private static readonly Guid SampleTimeline = Guid.Parse("0192a600-0000-7000-8000-000000000001");

    [HttpGet]
    public ActionResult<IReadOnlyList<TimelineDto>> ListByProject([FromQuery] Guid projectId)
    {
        var pid = projectId == Guid.Empty ? SampleProject : projectId;
        return Ok(new[]
        {
            new TimelineDto(SampleTimeline, pid, "Main Cut", DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddHours(-1)),
        });
    }

    [HttpPost]
    public ActionResult<TimelineDto> Create(CreateTimelineRequest request) =>
        Ok(new TimelineDto(Guid.CreateVersion7(), request.ProjectId, request.Name, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

    [HttpPost("{id:guid}/revisions")]
    public ActionResult<TimelineRevisionDto> AddRevision(Guid id, CreateRevisionRequest request) =>
        Ok(new TimelineRevisionDto(Guid.CreateVersion7(), id, 1, request.Operations, DateTimeOffset.UtcNow));

    [HttpPost("{id:guid}/render")]
    public ActionResult<RenderJobDto> RequestRender(Guid id) =>
        Ok(new RenderJobDto(Guid.CreateVersion7(), id, null, "queued", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

    [HttpGet("{id:guid}/commands")]
    public ActionResult<IReadOnlyList<CommandHistoryDto>> CommandHistory(Guid id) =>
        Ok(new[]
        {
            new CommandHistoryDto(Guid.CreateVersion7(), SampleProject, Guid.Parse("0192a3b4-c5d6-7e8f-9a0b-1c2d3e4f5061"), "trim clip 3 from 5 to 12 seconds", "concrete", DateTimeOffset.UtcNow.AddMinutes(-20)),
            new CommandHistoryDto(Guid.CreateVersion7(), SampleProject, Guid.Parse("0192a3b4-c5d6-7e8f-9a0b-1c2d3e4f5061"), "make this more engaging", "abstract", DateTimeOffset.UtcNow.AddMinutes(-5)),
        });
}
