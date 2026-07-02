using Kuvox.Api.Modules.Media.Enums;

namespace Kuvox.Api.Modules.Media.Contracts;

public sealed record IngestionRequestedEvent(
  Guid EventId,
  string EventType,
  DateTimeOffset OccurredAt,
  Guid MediaId,
  Guid OwnerId,
  OwnerKind OwnerKind,
  MediaKind Kind,
  OptimizedMediaObject Canonical,
  OptimizedMediaObject? Proxy,
  OptimizedMediaObject? Thumbnail,
  double? DurationSeconds,
  int? Width,
  int? Height,
  double? FrameRate,
  string? Codec
);
