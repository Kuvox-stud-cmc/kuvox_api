using Kuvox.Api.Modules.Media.Enums;

namespace Kuvox.Api.Modules.Media.Dtos;

public sealed record MediaDto(
    Guid Id,
    Guid OwnerId,
    OwnerKind OwnerKind,
    MediaKind Kind,
    Guid? ProjectId,
    string Filename,
    string StorageKey,
    long SizeBytes,
    string Status,
    double? DurationSeconds,
    int? Width,
    int? Height,
    string? Codec,
    DateTimeOffset CreatedAt);

/// <summary>
/// Registers an uploaded object as a media item pending ingestion. Owner (workspace + user) is
/// derived from the JWT + <c>studioId</c> query — never trusted from the body (plan §1).
/// </summary>
public sealed record RegisterMediaRequest(
    MediaKind Kind,
    Guid? ProjectId,
    string Filename,
    string StorageKey,
    long SizeBytes);

/// <summary>Grants another user (looked up by email) access to a media item.</summary>
public sealed record ShareMediaRequest(string Email, MediaRole Role);

/// <summary>A trashed media item plus how long until auto-purge removes it (7-day window).</summary>
public sealed record MediaTrashItemDto(
    Guid Id,
    MediaKind Kind,
    string Filename,
    DateTimeOffset DeletedAt,
    int PurgesInDays);
