namespace Kuvox.Api.Modules.Media.Contracts;

public sealed record OptimizedMediaObject(
    string BucketName,
    string ObjectKey,
    string ContentType,
    long SizeBytes
);

public sealed record MediaOptimizationCompletedEvent(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    Guid SourceEventId,
    Guid MediaId,
    OptimizedMediaObject? Canonical,
    OptimizedMediaObject? Proxy,
    OptimizedMediaObject? Thumbnail,
    double? DurationSeconds,
    int? Width,
    int? Height,
    double? FrameRate,
    string? Codec,
    string RawBucketName,
    string RawObjectKey,
    long RawSizeBytes
);

public sealed record MediaOptimizationFailedEvent(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    Guid SourceEventId,
    Guid MediaId,
    string ErrorCode,
    string ErrorMessage
);
