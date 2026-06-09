using Kuvox.Api.Modules.Media.Enums;
using Kuvox.Api.Modules.Media.Models;

namespace Kuvox.Api.Modules.Media.Repositories;

internal interface IMediaRepository
{
    Task<Models.Media?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Models.Media>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<int> CountByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Live (non-deleted) media owned by a workspace, newest-updated first, paged.</summary>
    Task<(IReadOnlyList<Models.Media> Items, int Total)> ListByWorkspaceAsync(
        OwnerKind ownerKind, Guid ownerId, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Live media shared with a user via <c>media_users</c> (excludes ones they own), paged.</summary>
    Task<(IReadOnlyList<(Models.Media Media, MediaRole Role)> Items, int Total)> ListSharedWithUserAsync(
        Guid userId, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Trashed (soft-deleted) media owned by a workspace, newest-deleted first, paged.</summary>
    Task<(IReadOnlyList<Models.Media> Items, int Total)> ListTrashAsync(
        OwnerKind ownerKind, Guid ownerId, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Media soft-deleted before <paramref name="cutoff"/> (for auto-purge).</summary>
    Task<IReadOnlyList<Models.Media>> ListDeletedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);

    Task<MediaUser?> GetMediaUserAsync(Guid mediaId, Guid userId, CancellationToken cancellationToken = default);

    Task AddAsync(Models.Media media, CancellationToken cancellationToken = default);

    Task AddMediaUserAsync(MediaUser mediaUser, CancellationToken cancellationToken = default);

    void RemoveMediaUser(MediaUser mediaUser);

    void Remove(Models.Media media);

    /// <summary>Deletes every media item belonging to a project (used on project deletion cleanup).</summary>
    Task<int> DeleteByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
