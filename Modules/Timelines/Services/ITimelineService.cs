using Kuvox.Api.Modules.Shared.Infrastructure;
using Kuvox.Api.Modules.Timelines.Dtos;

namespace Kuvox.Api.Modules.Timelines.Services;

/// <summary>
/// Module-internal business API of the Timelines module (scaffolded, not yet implemented).
/// Public only for the public controller's DI; impl stays <c>internal</c> (Rule 1). The
/// cross-module surface is <c>Timelines.Contracts</c> (Rule 2).
/// </summary>
public interface ITimelineService
{
    Task<TimelineDocumentDto> GetCurrentDocumentAsync(Guid projectId, CallerContext caller, CancellationToken cancellationToken = default);

    Task<TimelineDocumentDto> SaveCurrentDocumentAsync(Guid projectId, CallerContext caller, SaveTimelineDocumentRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TimelineDto>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<TimelineDto> CreateAsync(CreateTimelineRequest request, CancellationToken cancellationToken = default);

    Task<TimelineRevisionDto> AddRevisionAsync(Guid timelineId, CreateRevisionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Persists a queued render job for the latest synced timeline revision.</summary>
    Task<RenderJobDto> RequestRenderAsync(Guid timelineId, CallerContext caller, RenderTimelineRequest request, CancellationToken cancellationToken = default);

    /// <summary>Validates and logs client-side video editor performance metrics.</summary>
    Task RecordPerformanceAsync(Guid projectId, CallerContext caller, RecordVideoEditorPerformanceRequest request, CancellationToken cancellationToken = default);
}
