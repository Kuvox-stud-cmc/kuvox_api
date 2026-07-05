using Kuvox.Api.Modules.Media.Models;
using Kuvox.Api.Modules.Media.Enums;
using Microsoft.EntityFrameworkCore;

namespace Kuvox.Api.Modules.Media.Repositories;

internal sealed class AlbumRepository(MediaDbContext db) : IAlbumRepository
{
    public Task<Album?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Albums.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Album>> ListByUserAsync(Guid userId, bool includeSystem = false, CancellationToken cancellationToken = default)
    {
        return await (
            from au in db.AlbumUsers
            join a in db.Albums on au.AlbumId equals a.Id
            where au.UserId == userId && a.OwnerKind == OwnerKind.User && (includeSystem || a.IsDeleteAble)
            orderby a.CreatedAt descending
            select a
        ).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Album>> ListByWorkspaceAsync(
        OwnerKind ownerKind,
        Guid ownerId,
        Guid userId,
        bool includeSystem = false,
        CancellationToken cancellationToken = default)
    {
        if (ownerKind == OwnerKind.Studio)
        {
            return await db.Albums
                .Where(a => a.OwnerKind == ownerKind && a.OwnerId == ownerId && (includeSystem || a.IsDeleteAble))
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync(cancellationToken);
        }

        return await (
            from au in db.AlbumUsers
            join a in db.Albums on au.AlbumId equals a.Id
            where au.UserId == userId
                && a.OwnerKind == ownerKind
                && a.OwnerId == ownerId
                && (includeSystem || a.IsDeleteAble)
            orderby a.CreatedAt descending
            select a
        ).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<(Album Album, Permission Role)>> ListSharedWithUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var rows = await (
            from au in db.AlbumUsers
            join a in db.Albums on au.AlbumId equals a.Id
            where au.UserId == userId
                && !au.IsHidden
                && a.OwnerKind == OwnerKind.User
                && a.OwnerId != userId
                && a.IsDeleteAble
            orderby a.CreatedAt descending
            select new { Album = a, au.Role }
        ).ToListAsync(cancellationToken);

        return rows.Select(row => (row.Album, row.Role)).ToList();
    }

    public async Task<IReadOnlyList<Models.Media>> ListAlbumMediaAsync(Guid albumId, CancellationToken cancellationToken = default)
    {
        return await (
            from am in db.AlbumMedia
            join m in db.Media on am.MediaId equals m.Id
            where am.AlbumId == albumId && m.DeletedAt == null
            orderby m.CreatedAt descending
            select m
        ).ToListAsync(cancellationToken);
    }

    public async Task<bool> UserHasVisibleAlbumAccessToMediaAsync(
        Guid mediaId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await (
            from am in db.AlbumMedia
            join au in db.AlbumUsers on am.AlbumId equals au.AlbumId
            join a in db.Albums on am.AlbumId equals a.Id
            where am.MediaId == mediaId
                && au.UserId == userId
                && !au.IsHidden
                && a.OwnerKind == OwnerKind.User
                && a.IsDeleteAble
            select au
        ).AnyAsync(cancellationToken);
    }

    public Task<AlbumUser?> GetAlbumUserAsync(Guid albumId, Guid userId, CancellationToken cancellationToken = default) =>
        db.AlbumUsers.FirstOrDefaultAsync(au => au.AlbumId == albumId && au.UserId == userId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, bool>> GetFavoriteFlagsAsync(
        IEnumerable<Guid> albumIds,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var ids = albumIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, bool>();
        }

        return await db.AlbumUsers
            .Where(au => au.UserId == userId && ids.Contains(au.AlbumId))
            .ToDictionaryAsync(au => au.AlbumId, au => au.IsFavorite, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetMediaCountsAsync(
        IEnumerable<Guid> albumIds,
        CancellationToken cancellationToken = default)
    {
        var ids = albumIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var counts = new Dictionary<Guid, int>();
        await AddAlbumMediaCountsAsync(
            counts,
            db.AlbumPhotos.Where(am => ids.Contains(am.AlbumId)),
            cancellationToken);
        await AddAlbumMediaCountsAsync(
            counts,
            db.AlbumAudios.Where(am => ids.Contains(am.AlbumId)),
            cancellationToken);
        await AddAlbumMediaCountsAsync(
            counts,
            db.AlbumVideos.Where(am => ids.Contains(am.AlbumId)),
            cancellationToken);

        return counts;
    }

    private static async Task AddAlbumMediaCountsAsync<TAlbumMedia>(
        Dictionary<Guid, int> counts,
        IQueryable<TAlbumMedia> query,
        CancellationToken cancellationToken)
        where TAlbumMedia : AlbumMedia
    {
        var rows = await query
            .GroupBy(am => am.AlbumId)
            .Select(group => new { AlbumId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            counts[row.AlbumId] = counts.GetValueOrDefault(row.AlbumId) + row.Count;
        }
    }

    public void Add(Album album) => 
        db.Albums.Add(album);

    public void AddAlbumUser(AlbumUser albumUser) => 
        db.AlbumUsers.Add(albumUser);

    public void RemoveAlbumUser(AlbumUser albumUser) =>
        db.AlbumUsers.Remove(albumUser);

    public void Remove(Album album) => 
        db.Albums.Remove(album);

    public void AddAlbumMedia(AlbumMedia albumMedia) => 
        db.AlbumMedia.Add(albumMedia);

    public void RemoveAlbumMedia(AlbumMedia albumMedia) => 
        db.AlbumMedia.Remove(albumMedia);

    public Task<AlbumMedia?> GetAlbumMediaAsync(Guid albumId, Guid mediaId, CancellationToken cancellationToken = default) =>
        db.AlbumMedia.FirstOrDefaultAsync(am => am.AlbumId == albumId && am.MediaId == mediaId, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
