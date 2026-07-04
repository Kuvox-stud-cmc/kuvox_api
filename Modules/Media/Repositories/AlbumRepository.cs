using Kuvox.Api.Modules.Media.Models;
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
            where au.UserId == userId && (includeSystem || a.IsDeleteAble)
            orderby a.CreatedAt descending
            select a
        ).ToListAsync(cancellationToken);
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

    public void Add(Album album) => 
        db.Albums.Add(album);

    public void AddAlbumUser(AlbumUser albumUser) => 
        db.AlbumUsers.Add(albumUser);

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
