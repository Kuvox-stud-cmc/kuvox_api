using Kuvox.Api.Modules.Media.Dtos;
using Kuvox.Api.Modules.Media.Enums;
using Microsoft.AspNetCore.Mvc;

namespace Kuvox.Api.Modules.Media.Controllers;

/// <summary>Mockup Media endpoints returning canned data (no DB).</summary>
[ApiController]
[Route("api/mock/media")]
[Produces("application/json")]
public sealed class MediaMockupController : ControllerBase
{
    private static readonly Guid SampleOwner = Guid.Parse("0192a3b4-c5d6-7e8f-9a0b-1c2d3e4f5061");
    private static readonly Guid SampleProject = Guid.Parse("0192a400-0000-7000-8000-000000000001");

    private static readonly MediaDto[] Sample =
    [
        new(Guid.Parse("0192a500-0000-7000-8000-000000000001"), SampleOwner, OwnerKind.User, MediaKind.Video, SampleProject, "beach_drone.mp4", "raw/beach_drone.mp4", 184_320_000, "ready", 42.5, 3840, 2160, "h264", DateTimeOffset.UtcNow.AddDays(-2)),
        new(Guid.Parse("0192a500-0000-7000-8000-000000000002"), SampleOwner, OwnerKind.User, MediaKind.Video, SampleProject, "interview_a.mov", "raw/interview_a.mov", 2_400_000_000, "processing", 311.0, 1920, 1080, "prores", DateTimeOffset.UtcNow.AddHours(-5)),
    ];

    [HttpGet]
    public ActionResult<IReadOnlyList<MediaDto>> ListByProject([FromQuery] Guid projectId) =>
        Ok(Sample.Select(m => m with { ProjectId = projectId == Guid.Empty ? m.ProjectId : projectId }).ToArray());

    [HttpGet("{id:guid}")]
    public ActionResult<MediaDto> Get(Guid id) => Ok(Sample[0] with { Id = id });

    [HttpPost]
    public ActionResult<MediaDto> Register(RegisterMediaRequest request) =>
        Ok(new MediaDto(Guid.CreateVersion7(), SampleOwner, OwnerKind.User, request.Kind, request.ProjectId, request.Filename, request.StorageKey, request.SizeBytes, "uploaded", null, null, null, null, DateTimeOffset.UtcNow));
}
