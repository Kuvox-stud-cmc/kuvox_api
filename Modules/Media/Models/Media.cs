using Kuvox.Api.Modules.Media.Enums;
using Kuvox.Api.Modules.Shared.Models;
using System.ComponentModel.DataAnnotations.Schema;

namespace Kuvox.Api.Modules.Media.Models;

/// <summary>
/// A user/studio-owned media item (video, image, or audio) + extracted metadata. Owned by the
/// Media module (table <c>media.media</c>). <see cref="OwnerId"/> and <see cref="ProjectId"/>
/// reference other modules by id only (no cross-schema FK). Soft-deleted via
/// <see cref="DeletedAt"/> into Trash.
/// </summary>
public abstract class Media : BaseEntity
{
    [NotMapped]
    public abstract MediaKind Kind { get; }

    /// <summary>Owning workspace id (user or studio); see <see cref="OwnerKind"/>.</summary>
    public required Guid OwnerId { get; set; }

    public required OwnerKind OwnerKind { get; set; }

    /// <summary>Optional project this media belongs to. Stored as an id only; Projects owns the row.</summary>
    public Guid? ProjectId { get; set; }

    public required string Filename { get; set; }

    /// <summary>Object-storage key for the raw upload (S3/SeaweedFS).</summary>
    public required string StorageKey { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>Ingestion status mirror: uploaded | processing | ready | failed.</summary>
    public required MediaStatus Status { get; set; }

    /// <summary>
    /// Error message from the uploaded and ingestion pipeline, if any. Null if <see cref="Status"/> is not "failed".
    /// </summary>
    public string? ErrorMessage { get; set; }

    public string? Codec { get; set; }

    /// <summary>Soft-delete timestamp; non-null means the item is in Trash.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>Archive timestamp, storage key, and reason</summary>
    public DateTimeOffset? ArchivedAt { get; set; }

    public string? ArchiveStorageKey { get; set; }

    public string? ArchiveReason { get; set; }
}
