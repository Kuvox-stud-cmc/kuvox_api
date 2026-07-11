using Kuvox.Api.Modules.Timelines.Enums;
using Kuvox.Api.Modules.Timelines.Models;

namespace Kuvox.Api.Modules.Timelines.Services;

public sealed record RenderRealtimeUpdate(
    Guid JobId,
    Guid TimelineId,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? ErrorCode,
    string? ErrorMessage,
    bool OutputAvailable,
    string? OutputContentType,
    long? OutputSizeBytes,
    string Message)
{
    internal static RenderRealtimeUpdate FromJob(RenderJob job) =>
        new(
            job.Id,
            job.TimelineId,
            job.Status.ToString().ToLowerInvariant(),
            job.StartedAt,
            job.FinishedAt,
            job.CreatedAt,
            job.UpdatedAt,
            job.ErrorCode,
            job.ErrorMessage,
            job.Status == RenderStatus.Completed
                && !string.IsNullOrWhiteSpace(job.OutputBucketName)
                && !string.IsNullOrWhiteSpace(job.OutputStorageKey),
            job.OutputContentType,
            job.OutputSizeBytes,
            MessageFor(job));

    private static string MessageFor(RenderJob job) => job.Status switch
    {
        RenderStatus.Queued => "Render queued.",
        RenderStatus.Rendering => "Rendering video.",
        RenderStatus.Completed => "Export completed.",
        RenderStatus.Failed => string.IsNullOrWhiteSpace(job.ErrorMessage)
            ? "Render failed."
            : job.ErrorMessage,
        _ => "Render status updated."
    };
}
