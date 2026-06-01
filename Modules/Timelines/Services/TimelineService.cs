using Kuvox.Api.Modules.Timelines.Dtos;
using Kuvox.Api.Modules.Timelines.Repositories;

namespace Kuvox.Api.Modules.Timelines.Services;

/// <summary>Real Timelines business logic — SCAFFOLDED, NOT YET IMPLEMENTED (throws 501).</summary>
internal sealed class TimelineService(ITimelineRepository timelines) : ITimelineService
{
    private readonly ITimelineRepository _timelines = timelines;

    public Task<IReadOnlyList<TimelineDto>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<TimelineDto> CreateAsync(CreateTimelineRequest request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<TimelineRevisionDto> AddRevisionAsync(Guid timelineId, CreateRevisionRequest request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<RenderJobDto> RequestRenderAsync(Guid timelineId, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
