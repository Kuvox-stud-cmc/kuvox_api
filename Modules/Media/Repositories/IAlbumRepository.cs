using Kuvox.Api.Modules.Media.Models;
using Kuvox.Api.Modules.Media.Enums;

namespace Kuvox.Api.Modules.Media.Repositories;

internal interface IAlbumRepository
{
    Task<Album?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Album>> ListByUserAsync(Guid userId, bool includeSystem = false, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Album>> ListByWorkspaceAsync(
        OwnerKind ownerKind, Guid ownerId, Guid userId, bool includeSystem = false, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<(Album Album, Permission Role)>> ListSharedWithUserAsync(
        Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Models.Media>> ListAlbumMediaAsync(Guid albumId, CancellationToken cancellationToken = default);

    Task<bool> UserHasVisibleAlbumAccessToMediaAsync(Guid mediaId, Guid userId, CancellationToken cancellationToken = default);

    Task<AlbumUser?> GetAlbumUserAsync(Guid albumId, Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, bool>> GetFavoriteFlagsAsync(
        IEnumerable<Guid> albumIds, Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, int>> GetMediaCountsAsync(
        IEnumerable<Guid> albumIds, CancellationToken cancellationToken = default);

    void Add(Album album);

    void AddAlbumUser(AlbumUser albumUser);

    void RemoveAlbumUser(AlbumUser albumUser);

    void Remove(Album album);

    void AddAlbumMedia(AlbumMedia albumMedia);

    void RemoveAlbumMedia(AlbumMedia albumMedia);

    Task<AlbumMedia?> GetAlbumMediaAsync(Guid albumId, Guid mediaId, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
