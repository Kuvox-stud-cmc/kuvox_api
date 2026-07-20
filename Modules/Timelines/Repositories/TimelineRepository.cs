using Kuvox.Api.Modules.Shared.Infrastructure.Messaging;
using Kuvox.Api.Modules.Timelines.Models;
using Microsoft.EntityFrameworkCore;

namespace Kuvox.Api.Modules.Timelines.Repositories;

internal sealed class TimelineRepository(TimelinesDbContext db) : ITimelineRepository
{
    public Task<Timeline?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Timelines.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<Timeline?> GetByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        db.Timelines
            .OrderBy(t => t.CreatedAt)
            .FirstOrDefaultAsync(t => t.ProjectId == projectId, cancellationToken);

    public async Task<IReadOnlyList<Timeline>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        await db.Timelines.Where(t => t.ProjectId == projectId).ToListAsync(cancellationToken);

    public Task<int> CountByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        db.Timelines.CountAsync(t => t.ProjectId == projectId, cancellationToken);

    public Task<TimelineRevision?> GetLatestRevisionAsync(Guid timelineId, CancellationToken cancellationToken = default) =>
        db.TimelineRevisions
            .Where(revision => revision.TimelineId == timelineId)
            .OrderByDescending(revision => revision.RevisionNumber)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<TimelineRevisionIdentity?> GetCurrentRevisionIdentityAsync(
        Guid projectId,
        CancellationToken cancellationToken = default) =>
        (from timeline in db.Timelines
         where timeline.ProjectId == projectId
         from revision in db.TimelineRevisions
             .Where(revision => revision.TimelineId == timeline.Id)
             .OrderByDescending(revision => revision.RevisionNumber)
             .Take(1)
         orderby timeline.CreatedAt
         select new TimelineRevisionIdentity(
             projectId,
             timeline.Id,
             timeline.Name,
             timeline.CreatedAt,
             timeline.UpdatedAt,
             revision.Id,
             revision.RevisionNumber))
        .FirstOrDefaultAsync(cancellationToken);

    public Task<TimelineRevision?> GetRevisionByNumberAsync(Guid timelineId, int revisionNumber, CancellationToken cancellationToken = default) =>
        db.TimelineRevisions.FirstOrDefaultAsync(
            revision => revision.TimelineId == timelineId && revision.RevisionNumber == revisionNumber,
            cancellationToken);

    public Task<TimelineRevision?> GetRevisionByIdAsync(Guid revisionId, CancellationToken cancellationToken = default) =>
        db.TimelineRevisions.FirstOrDefaultAsync(revision => revision.Id == revisionId, cancellationToken);

    public Task<RenderJob?> GetRenderJobByIdAsync(Guid renderJobId, CancellationToken cancellationToken = default) =>
        db.RenderJobs.FirstOrDefaultAsync(job => job.Id == renderJobId, cancellationToken);

    public Task<RenderJobAccessState?> GetRenderJobAccessStateAsync(
        Guid renderJobId,
        CancellationToken cancellationToken = default) =>
        (from job in db.RenderJobs
         join timeline in db.Timelines on job.TimelineId equals timeline.Id
         join revision in db.TimelineRevisions on job.RevisionId equals revision.Id into revisions
         from revision in revisions.DefaultIfEmpty()
         where job.Id == renderJobId
         select new RenderJobAccessState(
             timeline.ProjectId,
             timeline.Id,
             job.RevisionId,
             revision == null ? null : revision.RevisionNumber,
             job.Status,
             job.UpdatedAt))
        .FirstOrDefaultAsync(cancellationToken);

    public async Task AddAsync(Timeline timeline, CancellationToken cancellationToken = default) =>
        await db.Timelines.AddAsync(timeline, cancellationToken);

    public async Task AddRevisionAsync(TimelineRevision revision, CancellationToken cancellationToken = default) =>
        await db.TimelineRevisions.AddAsync(revision, cancellationToken);

    public async Task AddRenderJobAsync(RenderJob renderJob, CancellationToken cancellationToken = default) =>
        await db.RenderJobs.AddAsync(renderJob, cancellationToken);

    public async Task EnqueueOutboxAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        var exists = await db.OutboxMessages
            .AnyAsync(existing => existing.DedupeKey == message.DedupeKey, cancellationToken);
        if (!exists)
        {
            await db.OutboxMessages.AddAsync(message, cancellationToken);
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
