using Kuvox.Api.Modules.Videos.Dtos;
using Kuvox.Api.Modules.Videos.Services;
using Microsoft.AspNetCore.Mvc;

namespace Kuvox.Api.Modules.Videos.Controllers;

/// <summary>
/// Real Videos endpoints, backed by the not-yet-implemented <see cref="IVideoService"/>
/// (returns <c>501</c>). Use <c>/api/mock/videos</c> for working fake data.
/// </summary>
[ApiController]
[Route("api/videos")]
[Produces("application/json")]
public sealed class VideosController(IVideoService videos) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<VideoDto>> ListByProject([FromQuery] Guid projectId, CancellationToken ct) =>
        videos.ListByProjectAsync(projectId, ct);

    [HttpGet("{id:guid}")]
    public Task<VideoDto?> Get(Guid id, CancellationToken ct) => videos.GetAsync(id, ct);

    [HttpPost]
    public Task<VideoDto> Register(RegisterVideoRequest request, CancellationToken ct) =>
        videos.RegisterAsync(request, ct);
}
