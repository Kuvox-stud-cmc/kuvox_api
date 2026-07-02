using Kuvox.Api.Modules.Media.Enums;

namespace Kuvox.Api.Modules.Media.Contracts;

public sealed record MediaOptimizationRequestedEvent(
  Guid EventId,
  string EventType,
  DateTimeOffset OccurredAt,
  Guid MediaId,
  Guid ProjectId,
  Guid UserId,
  string BucketName,
  string ObjectKey,
  string ContentType,
  string OriginalFileName,
  long SizeBytes,
  MediaKind Kind
);
