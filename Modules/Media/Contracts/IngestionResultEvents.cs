namespace Kuvox.Api.Modules.Media.Contracts;

public sealed record IngestionCompletedEvent(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    Guid SourceEventId,
    Guid MediaId,
    int ShotCount
);

public sealed record IngestionFailedEvent(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    Guid SourceEventId,
    Guid MediaId,
    string ErrorCode,
    string ErrorMessage
);
