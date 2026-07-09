namespace Kuvox.Api.Modules.Timelines.Contracts;

using System.Text.Json;

public sealed record RenderingMediaSource(
    Guid MediaId,
    string Kind,
    string? BucketName,
    string ObjectKey,
    string? ContentType,
    long SizeBytes,
    double? DurationSeconds,
    int? Width,
    int? Height,
    double? FrameRate,
    string? Codec);

public sealed record RenderingRequestedEvent(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    Guid RenderJobId,
    Guid TimelineId,
    Guid ProjectId,
    Guid RevisionId,
    int RevisionNumber,
    Guid RequestedByUserId,
    JsonElement Settings,
    JsonElement DocumentJson,
    IReadOnlyList<RenderingMediaSource> MediaSources,
    string OutputBucketName,
    string OutputStorageKey,
    string OutputContentType);

public sealed record RenderingStartedEvent(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    Guid SourceEventId,
    Guid RenderJobId,
    DateTimeOffset StartedAt);

public sealed record RenderingCompletedEvent(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    Guid SourceEventId,
    Guid RenderJobId,
    string OutputBucketName,
    string OutputStorageKey,
    string OutputContentType,
    long OutputSizeBytes,
    DateTimeOffset FinishedAt);

public sealed record RenderingFailedEvent(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    Guid SourceEventId,
    Guid RenderJobId,
    string ErrorCode,
    string ErrorMessage,
    DateTimeOffset FinishedAt);
