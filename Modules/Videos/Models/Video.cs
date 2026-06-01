using Kuvox.Api.Modules.Shared.Models;

namespace Kuvox.Api.Modules.Videos.Models;

/// <summary>
/// Uploaded source video + extracted metadata. Owned by the Videos module
/// (table <c>videos.videos</c>). <see cref="ProjectId"/> references the Projects module by
/// id only (no cross-schema FK).
/// </summary>
public sealed class Video : BaseEntity
{
    public required Guid ProjectId { get; set; }

    public required string Filename { get; set; }

    /// <summary>Object-storage key for the raw upload (S3/MinIO).</summary>
    public required string StorageKey { get; set; }

    public double DurationSeconds { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public string? Codec { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>Ingestion status mirror: uploaded | processing | ready | failed.</summary>
    public string Status { get; set; } = "uploaded";
}
