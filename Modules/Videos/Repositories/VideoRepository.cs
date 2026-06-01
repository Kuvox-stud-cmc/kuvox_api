using Kuvox.Api.Modules.Videos.Models;
using Microsoft.EntityFrameworkCore;

namespace Kuvox.Api.Modules.Videos.Repositories;

internal sealed class VideoRepository(VideosDbContext db) : IVideoRepository
{
    public Task<Video?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Videos.FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Video>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        await db.Videos.Where(v => v.ProjectId == projectId).ToListAsync(cancellationToken);

    public Task<int> CountByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        db.Videos.CountAsync(v => v.ProjectId == projectId, cancellationToken);

    public async Task AddAsync(Video video, CancellationToken cancellationToken = default) =>
        await db.Videos.AddAsync(video, cancellationToken);

    public Task<int> DeleteByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        db.Videos.Where(v => v.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
