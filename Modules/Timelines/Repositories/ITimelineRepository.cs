using Kuvox.Api.Modules.Timelines.Models;
using Kuvox.Api.Modules.Shared.Infrastructure.Messaging;

namespace Kuvox.Api.Modules.Timelines.Repositories;

internal interface ITimelineRepository
{
    Task<Timeline?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Timeline?> GetByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Timeline>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<int> CountByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<TimelineRevision?> GetLatestRevisionAsync(Guid timelineId, CancellationToken cancellationToken = default);

    Task<TimelineRevision?> GetRevisionByNumberAsync(Guid timelineId, int revisionNumber, CancellationToken cancellationToken = default);

    Task<TimelineRevision?> GetRevisionByIdAsync(Guid revisionId, CancellationToken cancellationToken = default);

    Task<RenderJob?> GetRenderJobByIdAsync(Guid renderJobId, CancellationToken cancellationToken = default);

    Task AddAsync(Timeline timeline, CancellationToken cancellationToken = default);

    Task AddRevisionAsync(TimelineRevision revision, CancellationToken cancellationToken = default);

    Task AddRenderJobAsync(RenderJob renderJob, CancellationToken cancellationToken = default);

    Task EnqueueOutboxAsync(OutboxMessage message, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
