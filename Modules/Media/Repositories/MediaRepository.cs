using Kuvox.Api.Modules.Media.Enums;
using Kuvox.Api.Modules.Media.Models;
using Microsoft.EntityFrameworkCore;

namespace Kuvox.Api.Modules.Media.Repositories;

internal sealed class MediaRepository(MediaDbContext db) : IMediaRepository
{
    public Task<Models.Media?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Media.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Models.Media>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        await db.Media.Where(m => m.ProjectId == projectId).ToListAsync(cancellationToken);

    public Task<int> CountByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        db.Media.CountAsync(m => m.ProjectId == projectId, cancellationToken);

    public async Task<(IReadOnlyList<Models.Media> Items, int Total)> ListByWorkspaceAsync(
        OwnerKind ownerKind, Guid ownerId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = db.Media
            .Where(m => m.OwnerKind == ownerKind && m.OwnerId == ownerId && m.DeletedAt == null);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(m => m.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<(IReadOnlyList<(Models.Media Media, MediaRole Role)> Items, int Total)> ListSharedWithUserAsync(
        Guid userId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query =
            from mu in db.MediaUsers
            join m in db.Media on mu.MediaId equals m.Id
            where mu.UserId == userId && m.DeletedAt == null && m.OwnerId != userId
            orderby m.UpdatedAt descending
            select new { Media = m, mu.Role };

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (rows.Select(r => (r.Media, r.Role)).ToList(), total);
    }

    public async Task<(IReadOnlyList<Models.Media> Items, int Total)> ListTrashAsync(
        OwnerKind ownerKind, Guid ownerId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = db.Media
            .Where(m => m.OwnerKind == ownerKind && m.OwnerId == ownerId && m.DeletedAt != null);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(m => m.DeletedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<IReadOnlyList<Models.Media>> ListDeletedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default) =>
        await db.Media.Where(m => m.DeletedAt != null && m.DeletedAt < cutoff).ToListAsync(cancellationToken);

    public Task<MediaUser?> GetMediaUserAsync(Guid mediaId, Guid userId, CancellationToken cancellationToken = default) =>
        db.MediaUsers.FirstOrDefaultAsync(mu => mu.MediaId == mediaId && mu.UserId == userId, cancellationToken);

    public async Task AddAsync(Models.Media media, CancellationToken cancellationToken = default) =>
        await db.Media.AddAsync(media, cancellationToken);

    public async Task AddMediaUserAsync(MediaUser mediaUser, CancellationToken cancellationToken = default) =>
        await db.MediaUsers.AddAsync(mediaUser, cancellationToken);

    public void RemoveMediaUser(MediaUser mediaUser) => db.MediaUsers.Remove(mediaUser);

    public void Remove(Models.Media media) => db.Media.Remove(media);

    public Task<int> DeleteByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        db.Media.Where(m => m.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
