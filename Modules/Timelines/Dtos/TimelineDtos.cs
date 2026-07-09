namespace Kuvox.Api.Modules.Timelines.Dtos;

using System.Text.Json;

public sealed record TimelineDto(Guid Id, Guid ProjectId, string Name, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record CreateTimelineRequest(Guid ProjectId, string Name);

public sealed record TimelineRevisionDto(Guid Id, Guid TimelineId, int RevisionNumber, string Operations, DateTimeOffset CreatedAt);

/// <summary>Appends a new revision (the JSONB operations payload) to a timeline.</summary>
public sealed record CreateRevisionRequest(string Operations);

public sealed record RenderTimelineRequest(
    Guid TimelineId,
    int RevisionNumber,
    JsonElement Settings);

public sealed record RenderJobDto(
    Guid Id,
    Guid TimelineId,
    Guid? RevisionId,
    int? RevisionNumber,
    string Status,
    string? OutputBucketName,
    string? OutputStorageKey,
    string? OutputContentType,
    long? OutputSizeBytes,
    string? OutputUrl,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record VideoEditorPerformanceMetricDto(
    string Name,
    double DurationMs,
    DateTimeOffset MeasuredAt,
    int? TrackCount,
    int? ItemCount,
    int? RenderedItemCount,
    double? TimelineDurationSeconds);

public sealed record RecordVideoEditorPerformanceRequest(
    IReadOnlyList<VideoEditorPerformanceMetricDto> Metrics);

public sealed record CommandHistoryDto(Guid Id, Guid ProjectId, Guid UserId, string CommandText, string? Intent, DateTimeOffset CreatedAt);

public sealed record TimelineDocumentDto(
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

public sealed record SaveTimelineDocumentRequest(
    JsonElement DocumentJson,
    JsonElement OperationsJson,
    int BaseRevisionNumber,
    int DocumentSchemaVersion,
    string? Source,
    string? Label);
