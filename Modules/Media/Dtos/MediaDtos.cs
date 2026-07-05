using Kuvox.Api.Modules.Media.Enums;

namespace Kuvox.Api.Modules.Media.Dtos;

public sealed record MediaDto(
    Guid Id,
    Guid OwnerId,
    OwnerKind OwnerKind,
    string? OwnerEmail,
    string? OwnerDisplayName,
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
    DateTimeOffset CreatedAt,
    bool IsFavorite,
    MediaPipelineDto Pipeline
);

public sealed record MediaPipelineDto(
    string Stage,
    string Label,
    string Detail,
    int Step,
    int StepCount,
    bool Terminal
);

public sealed record UploadMediaRequest(
    IFormFile File,
    MediaKind Kind,
    string? Filename
);

/// <summary>Grants another user (looked up by email) access to a media item.</summary>
public sealed record ShareMediaRequest(string Email, Permission Role);

public sealed record UpdateMediaAccessRequest(Guid UserId, Permission? Role, bool IsHidden);

public sealed record MediaAccessMemberDto(
    Guid UserId,
    string Email,
    string DisplayName,
    string StudioRole,
    Permission EffectiveRole,
    Permission? OverrideRole,
    bool IsHidden,
    bool CanManage);

public sealed record ToggleMediaFavoriteRequest(bool IsFavorite);

public sealed record MediaStorageUsageDto(
    string Plan,
    long StorageBytesUsed,
    long StorageBytesQuota,
    double StoragePercent,
    int MediaCount,
    long ActiveBytesUsed,
    long TrashBytesUsed,
    MediaStorageObjectBreakdownDto ObjectBreakdown,
    MediaStorageObjectBreakdownDto TrashObjectBreakdown
);

public sealed record MediaStorageObjectBreakdownDto(
    long RawBytes,
    long CanonicalBytes,
    long ProxyBytes,
    long ThumbnailBytes
);

/// <summary>A trashed media item plus how long until auto-purge removes it (7-day window).</summary>
public sealed record MediaTrashItemDto(
    Guid Id,
    MediaKind Kind,
    string Filename,
    DateTimeOffset DeletedAt,
    int PurgesInDays);
