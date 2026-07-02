namespace Kuvox.Api.Modules.Media.Contracts;

/// <summary>Public cross-module API of the Media module (Rule 2).</summary>
public interface IMediaApi
{
    Task<MediaSummary?> GetSummaryAsync(Guid mediaId, CancellationToken cancellationToken = default);
}
