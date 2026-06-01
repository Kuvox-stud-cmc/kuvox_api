namespace Kuvox.Api.Modules.Timelines.Contracts;

/// <summary>Public cross-module API of the Timelines module (Rule 2).</summary>
public interface ITimelinesApi
{
    Task<int> CountByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<TimelineSummary?> GetSummaryAsync(Guid timelineId, CancellationToken cancellationToken = default);
}
