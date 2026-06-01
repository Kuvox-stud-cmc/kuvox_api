namespace Kuvox.Api.Modules.Videos.Dtos;

public sealed record VideoDto(
    Guid Id,
    Guid ProjectId,
    string Filename,
    string StorageKey,
    double DurationSeconds,
    int Width,
    int Height,
    string? Codec,
    long SizeBytes,
    string Status,
    DateTimeOffset CreatedAt);

/// <summary>Registers an uploaded object as a video pending ingestion.</summary>
public sealed record RegisterVideoRequest(Guid ProjectId, string Filename, string StorageKey, long SizeBytes);
