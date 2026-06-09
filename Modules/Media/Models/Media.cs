using Kuvox.Api.Modules.Media.Enums;
using Kuvox.Api.Modules.Shared.Models;

namespace Kuvox.Api.Modules.Media.Models;

/// <summary>
/// A user/studio-owned media item (video, image, or audio) + extracted metadata. Owned by the
/// Media module (table <c>media.media</c>). <see cref="OwnerId"/> and <see cref="ProjectId"/>
/// reference other modules by id only (no cross-schema FK). Soft-deleted via
/// <see cref="DeletedAt"/> into Trash.
/// </summary>
public sealed class Media : BaseEntity
{
    /// <summary>Owning workspace id (user or studio); see <see cref="OwnerKind"/>.</summary>
    public required Guid OwnerId { get; set; }

    public required OwnerKind OwnerKind { get; set; }

    public required MediaKind Kind { get; set; }

    /// <summary>Optional link to a project that uses this media (references Projects by id only).</summary>
    public Guid? ProjectId { get; set; }

    public required string Filename { get; set; }

    /// <summary>Object-storage key for the raw upload (S3/MinIO).</summary>
    public required string StorageKey { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>Ingestion status mirror: uploaded | processing | ready | failed.</summary>
    public string Status { get; set; } = "uploaded";

    /// <summary>Video/audio only.</summary>
    public double? DurationSeconds { get; set; }

    /// <summary>Video/image only.</summary>
    public int? Width { get; set; }

    public int? Height { get; set; }

    public string? Codec { get; set; }

    /// <summary>Soft-delete timestamp; non-null means the item is in Trash.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
