using Kuvox.Api.Modules.Media.Enums;

namespace Kuvox.Api.Modules.Media.Dtos;

public sealed record MediaDto(
    Guid Id,
    Guid OwnerId,
    OwnerKind OwnerKind,
    MediaKind Kind,
    string Filename,
    string StorageKey,
    long SizeBytes,
    string Status,
    string? CanonicalStorageKey,
    string? ProxyStorageKey,
    string? ThumbnailStorageKey,
    string? ErrorMessage,
    double? DurationSeconds,
    int? Width,
    int? Height,
    string? Codec,
    double? FrameRate,
    DateTimeOffset CreatedAt
);

public sealed record UploadMediaRequest(
    IFormFile File,
    MediaKind Kind
);

/// <summary>Grants another user (looked up by email) access to a media item.</summary>
public sealed record ShareMediaRequest(string Email, Permission Role);

/// <summary>A trashed media item plus how long until auto-purge removes it (7-day window).</summary>
public sealed record MediaTrashItemDto(
    Guid Id,
    MediaKind Kind,
    string Filename,
    DateTimeOffset DeletedAt,
    int PurgesInDays);
