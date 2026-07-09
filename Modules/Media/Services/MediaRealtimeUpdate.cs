using Kuvox.Api.Modules.Media.Dtos;

namespace Kuvox.Api.Modules.Media.Services;

internal sealed record MediaRealtimeUpdate(
    MediaDto Media,
    string Phase,
    DateTimeOffset OccurredAt,
    MediaPipelineDto Pipeline,
    int? ShotCount = null,
    int? VisualCount = null,
    int? AudioCount = null,
    int? TranscriptCount = null,
    int? OcrCount = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);
