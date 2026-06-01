using Kuvox.Api.Modules.Timelines.Models;
using Microsoft.EntityFrameworkCore;

namespace Kuvox.Api.Modules.Timelines.Repositories;

internal sealed class TimelineRepository(TimelinesDbContext db) : ITimelineRepository
{
    public Task<Timeline?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Timelines.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Timeline>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        await db.Timelines.Where(t => t.ProjectId == projectId).ToListAsync(cancellationToken);

    public Task<int> CountByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        db.Timelines.CountAsync(t => t.ProjectId == projectId, cancellationToken);

    public async Task AddAsync(Timeline timeline, CancellationToken cancellationToken = default) =>
        await db.Timelines.AddAsync(timeline, cancellationToken);

    public Task<int> DeleteByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        db.Timelines.Where(t => t.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
