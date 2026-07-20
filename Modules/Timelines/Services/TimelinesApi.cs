using Kuvox.Api.Modules.Timelines.Contracts;
using Kuvox.Api.Modules.Projects.Contracts;
using Kuvox.Api.Modules.Timelines.Repositories;
using Kuvox.Api.Modules.Shared.Infrastructure.Caching;
using System.Text.Json;

namespace Kuvox.Api.Modules.Timelines.Services;

/// <summary>Implements the public <see cref="ITimelinesApi"/> read facade (Rule 2). Internal (Rule 1).</summary>
internal sealed class TimelinesApi(ITimelineRepository timelines, EditorDocumentCache cache) : ITimelinesApi
{
    public Task<int> CountByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        timelines.CountByProjectAsync(projectId, cancellationToken);

    public async Task<TimelineSummary?> GetSummaryAsync(Guid timelineId, CancellationToken cancellationToken = default)
    {
        var timeline = await timelines.GetByIdAsync(timelineId, cancellationToken);
        return timeline is null ? null : new TimelineSummary(timeline.Id, timeline.ProjectId, timeline.Name);
    }

    public async Task<AuthorizedTimelineDocumentSnapshot?> GetAuthorizedProjectSnapshotAsync(
        ProjectDocumentAccess project,
        CancellationToken cancellationToken = default)
    {
        if (project.Kind != ProjectContentKind.Video)
        {
            return null;
        }

        if (!cache.DocumentsEnabled)
        {
            var timeline = await timelines.GetByProjectAsync(project.Id, cancellationToken);
            if (timeline is null) return null;
            var revision = await timelines.GetLatestRevisionAsync(timeline.Id, cancellationToken);
            return revision is null ? null : ToSnapshot(project.Id, timeline.Id, revision);
        }

        var identity = await timelines.GetCurrentRevisionIdentityAsync(project.Id, cancellationToken);
        if (identity is null) return null;
        return await cache.GetTimelineDocumentAsync(
            project.Id,
            identity.RevisionNumber,
            async ct =>
            {
                var revision = await timelines.GetRevisionByIdAsync(identity.RevisionId, ct)
                    ?? throw new InvalidOperationException("The authoritative timeline revision disappeared.");
                return ToSnapshot(project.Id, identity.TimelineId, revision);
            },
            cancellationToken);
    }

    private static AuthorizedTimelineDocumentSnapshot ToSnapshot(Guid projectId, Guid timelineId, Models.TimelineRevision revision)
    {
        using var document = JsonDocument.Parse(revision.DocumentJson);
        return new AuthorizedTimelineDocumentSnapshot(
            projectId,
            timelineId,
            revision.Id,
            document.RootElement.Clone(),
            revision.RevisionNumber,
            revision.DocumentSchemaVersion,
            revision.Source,
            revision.Label,
            revision.CreatedAt,
            revision.CreatedByUserId);
    }
}
