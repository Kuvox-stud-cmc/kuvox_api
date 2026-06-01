using Kuvox.Api.Modules.Videos.Contracts;
using Kuvox.Api.Modules.Videos.Repositories;

namespace Kuvox.Api.Modules.Videos.Services;

/// <summary>Implements the public <see cref="IVideosApi"/> read facade (Rule 2). Internal (Rule 1).</summary>
internal sealed class VideosApi(IVideoRepository videos) : IVideosApi
{
    public Task<int> CountByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        videos.CountByProjectAsync(projectId, cancellationToken);

    public async Task<VideoSummary?> GetSummaryAsync(Guid videoId, CancellationToken cancellationToken = default)
    {
        var video = await videos.GetByIdAsync(videoId, cancellationToken);
        return video is null
            ? null
            : new VideoSummary(video.Id, video.ProjectId, video.Filename, video.DurationSeconds, video.Status);
    }
}
