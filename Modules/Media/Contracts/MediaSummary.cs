using Kuvox.Api.Modules.Media.Enums;

namespace Kuvox.Api.Modules.Media.Contracts;

/// <summary>Shareable media projection for other modules (Rule 2).</summary>
public sealed record MediaSummary(
    Guid Id,
    Guid OwnerId,
    OwnerKind OwnerKind,
    MediaKind Kind,
    string Filename,
    string Status,
    bool IsDeleted = false,
    string? StorageKey = null,
    long SizeBytes = 0,
    string? CanonicalStorageKey = null,
    string? ProxyStorageKey = null,
    string? ThumbnailStorageKey = null,
    string? ErrorMessage = null,
    double? DurationSeconds = null,
    int? Width = null,
    int? Height = null,
    string? Codec = null,
    double? FrameRate = null,
    DateTimeOffset? CreatedAt = null
);

public enum MediaResolutionAvailability
{
    Available,
    Processing,
    Failed,
    Deleted,
    Inaccessible,
    Missing
}

public sealed record MediaResolution(
    Guid MediaId,
    MediaKind? Kind,
    MediaResolutionAvailability Availability,
    MediaSummary? Media
);
