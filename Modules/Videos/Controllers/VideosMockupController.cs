using Kuvox.Api.Modules.Videos.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Kuvox.Api.Modules.Videos.Controllers;

/// <summary>Mockup Videos endpoints returning canned data (no DB).</summary>
[ApiController]
[Route("api/mock/videos")]
[Produces("application/json")]
public sealed class VideosMockupController : ControllerBase
{
    private static readonly Guid SampleProject = Guid.Parse("0192a400-0000-7000-8000-000000000001");

    private static readonly VideoDto[] Sample =
    [
        new(Guid.Parse("0192a500-0000-7000-8000-000000000001"), SampleProject, "beach_drone.mp4", "raw/beach_drone.mp4", 42.5, 3840, 2160, "h264", 184_320_000, "ready", DateTimeOffset.UtcNow.AddDays(-2)),
        new(Guid.Parse("0192a500-0000-7000-8000-000000000002"), SampleProject, "interview_a.mov", "raw/interview_a.mov", 311.0, 1920, 1080, "prores", 2_400_000_000, "processing", DateTimeOffset.UtcNow.AddHours(-5)),
    ];

    [HttpGet]
    public ActionResult<IReadOnlyList<VideoDto>> ListByProject([FromQuery] Guid projectId) =>
        Ok(Sample.Select(v => v with { ProjectId = projectId == Guid.Empty ? v.ProjectId : projectId }).ToArray());

    [HttpGet("{id:guid}")]
    public ActionResult<VideoDto> Get(Guid id) => Ok(Sample[0] with { Id = id });

    [HttpPost]
    public ActionResult<VideoDto> Register(RegisterVideoRequest request) =>
        Ok(new VideoDto(Guid.CreateVersion7(), request.ProjectId, request.Filename, request.StorageKey, 0, 0, 0, null, request.SizeBytes, "uploaded", DateTimeOffset.UtcNow));
}
