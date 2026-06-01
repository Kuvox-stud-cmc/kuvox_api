using Kuvox.Api.Modules.Videos.Dtos;
using Kuvox.Api.Modules.Videos.Repositories;

namespace Kuvox.Api.Modules.Videos.Services;

/// <summary>Real Videos business logic — SCAFFOLDED, NOT YET IMPLEMENTED (throws 501).</summary>
internal sealed class VideoService(IVideoRepository videos) : IVideoService
{
    private readonly IVideoRepository _videos = videos;

    public Task<IReadOnlyList<VideoDto>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<VideoDto?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<VideoDto> RegisterAsync(RegisterVideoRequest request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
