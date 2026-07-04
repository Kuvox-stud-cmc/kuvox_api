using Kuvox.Api.Modules.Media.Enums;
using Kuvox.Api.Modules.Media.Models;
using Kuvox.Api.Modules.Shared.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Kuvox.Api.Modules.Media.Repositories;

internal sealed class MediaRepository(MediaDbContext db) : IMediaRepository
{
    public Task<Models.Media?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Media.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

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

    public async Task<(IReadOnlyList<(Models.Media Media, Permission Role)> Items, int Total)> ListSharedWithUserAsync(
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

    public async Task<IReadOnlyList<Models.Media>> ListStalePipelineAsync(
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken = default) =>
        await db.Media
            .Where(m =>
                m.DeletedAt == null
                && m.UpdatedAt <= cutoff
                && (m.Status == MediaStatus.Uploaded || m.Status == MediaStatus.Processing))
            .OrderBy(m => m.UpdatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

    public Task<MediaUser?> GetMediaUserAsync(Guid mediaId, Guid userId, CancellationToken cancellationToken = default) =>
        db.MediaUsers.FirstOrDefaultAsync(mu => mu.MediaId == mediaId && mu.UserId == userId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, bool>> GetFavoriteFlagsAsync(
        IEnumerable<Guid> mediaIds,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var ids = mediaIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, bool>();
        }

        return await db.MediaUsers
            .Where(mu => mu.UserId == userId && ids.Contains(mu.MediaId))
            .ToDictionaryAsync(mu => mu.MediaId, mu => mu.IsFavorite, cancellationToken);
    }

    public async Task AddAsync(Models.Media media, CancellationToken cancellationToken = default) =>
        await db.Media.AddAsync(media, cancellationToken);

    public async Task AddMediaUserAsync(MediaUser mediaUser, CancellationToken cancellationToken = default) =>
        await db.MediaUsers.AddAsync(mediaUser, cancellationToken);

    public async Task EnqueueOutboxAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        var exists = await db.OutboxMessages
            .AnyAsync(existing => existing.DedupeKey == message.DedupeKey, cancellationToken);
        if (exists)
        {
            return;
        }

        await db.OutboxMessages.AddAsync(message, cancellationToken);
    }

    public async Task EnsurePendingOutboxAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        var existing = await db.OutboxMessages
            .FirstOrDefaultAsync(row => row.DedupeKey == message.DedupeKey, cancellationToken);
        if (existing is null)
        {
            await db.OutboxMessages.AddAsync(message, cancellationToken);
            return;
        }

        if (existing.Status == OutboxMessageStatus.Pending)
        {
            return;
        }

        existing.Transport = message.Transport;
        existing.Exchange = message.Exchange;
        existing.RoutingKey = message.RoutingKey;
        existing.EventType = message.EventType;
        existing.PayloadJson = message.PayloadJson;
        existing.HeadersJson = message.HeadersJson;
        existing.Status = OutboxMessageStatus.Pending;
        existing.AttemptCount = 0;
        existing.NextAttemptAt = DateTimeOffset.UtcNow;
        existing.LockedUntil = null;
        existing.LastError = null;
        existing.PublishedAt = null;
    }

    public void RemoveMediaUser(MediaUser mediaUser) => db.MediaUsers.Remove(mediaUser);

    public void Remove(Models.Media media) => db.Media.Remove(media);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
