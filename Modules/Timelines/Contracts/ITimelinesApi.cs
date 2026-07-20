using Kuvox.Api.Modules.Projects.Contracts;
using System.Text.Json;

namespace Kuvox.Api.Modules.Timelines.Contracts;

/// <summary>Public cross-module API of the Timelines module (Rule 2).</summary>
public interface ITimelinesApi
{
    Task<int> CountByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<TimelineSummary?> GetSummaryAsync(Guid timelineId, CancellationToken cancellationToken = default);

    Task<AuthorizedTimelineDocumentSnapshot?> GetAuthorizedProjectSnapshotAsync(
        ProjectDocumentAccess project,
        CancellationToken cancellationToken = default);
}

public sealed record AuthorizedTimelineDocumentSnapshot(
    Guid ProjectId,
    Guid TimelineId,
    Guid RevisionId,
    JsonElement DocumentJson,
    int RevisionNumber,
    int DocumentSchemaVersion,
    string? Source,
    string? Label,
    DateTimeOffset UpdatedAt,
    Guid UpdatedByUserId);
