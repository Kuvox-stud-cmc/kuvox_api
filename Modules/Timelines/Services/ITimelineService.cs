using Kuvox.Api.Modules.Timelines.Dtos;

namespace Kuvox.Api.Modules.Timelines.Services;

/// <summary>
/// Module-internal business API of the Timelines module (scaffolded, not yet implemented).
/// Public only for the public controller's DI; impl stays <c>internal</c> (Rule 1). The
/// cross-module surface is <c>Timelines.Contracts</c> (Rule 2).
/// </summary>
public interface ITimelineService
{
    Task<IReadOnlyList<TimelineDto>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<TimelineDto> CreateAsync(CreateTimelineRequest request, CancellationToken cancellationToken = default);

    Task<TimelineRevisionDto> AddRevisionAsync(Guid timelineId, CreateRevisionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Persists a revision and dispatches a render job to the AI service via RabbitMQ.</summary>
    Task<RenderJobDto> RequestRenderAsync(Guid timelineId, CancellationToken cancellationToken = default);
}
