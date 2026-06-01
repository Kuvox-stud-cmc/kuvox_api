using Kuvox.Api.Modules.Timelines.Contracts;
using Kuvox.Api.Modules.Timelines.Repositories;

namespace Kuvox.Api.Modules.Timelines.Services;

/// <summary>Implements the public <see cref="ITimelinesApi"/> read facade (Rule 2). Internal (Rule 1).</summary>
internal sealed class TimelinesApi(ITimelineRepository timelines) : ITimelinesApi
{
    public Task<int> CountByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        timelines.CountByProjectAsync(projectId, cancellationToken);

    public async Task<TimelineSummary?> GetSummaryAsync(Guid timelineId, CancellationToken cancellationToken = default)
    {
        var timeline = await timelines.GetByIdAsync(timelineId, cancellationToken);
        return timeline is null ? null : new TimelineSummary(timeline.Id, timeline.ProjectId, timeline.Name);
    }
}
