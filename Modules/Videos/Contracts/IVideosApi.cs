namespace Kuvox.Api.Modules.Videos.Contracts;

/// <summary>Public cross-module API of the Videos module (Rule 2).</summary>
public interface IVideosApi
{
    Task<int> CountByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<VideoSummary?> GetSummaryAsync(Guid videoId, CancellationToken cancellationToken = default);
}
