using Kuvox.Api.Modules.Media.Models;

namespace Kuvox.Api.Modules.Media.Repositories;

internal interface IAlbumRepository
{
    Task<Album?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Album>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Models.Media>> ListAlbumMediaAsync(Guid albumId, CancellationToken cancellationToken = default);

    Task<AlbumUser?> GetAlbumUserAsync(Guid albumId, Guid userId, CancellationToken cancellationToken = default);

    void Add(Album album);

    void AddAlbumUser(AlbumUser albumUser);

    void Remove(Album album);

    void AddAlbumMedia(AlbumMedia albumMedia);

    void RemoveAlbumMedia(AlbumMedia albumMedia);

    Task<AlbumMedia?> GetAlbumMediaAsync(Guid albumId, Guid mediaId, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
