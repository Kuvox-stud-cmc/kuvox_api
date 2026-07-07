using Kuvox.Api.Modules.Media.Enums;
using Kuvox.Api.Modules.Media.Models;
using Kuvox.Api.Modules.Shared.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kuvox.Api.Modules.Media.Repositories;

internal interface IMediaRepository
{
    Task<Models.Media?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Models.Media>> ListByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default);

    /// <summary>Live (non-deleted) media owned by a workspace, newest-updated first, paged.</summary>
    Task<(IReadOnlyList<Models.Media> Items, int Total)> ListByWorkspaceAsync(
        OwnerKind ownerKind, Guid ownerId, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Live media shared with a user via <c>media_users</c> (excludes ones they own), paged.</summary>
    Task<(IReadOnlyList<(Models.Media Media, Permission Role)> Items, int Total)> ListSharedWithUserAsync(
        Guid userId, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Trashed (soft-deleted) media owned by a workspace, newest-deleted first, paged.</summary>
    Task<(IReadOnlyList<Models.Media> Items, int Total)> ListTrashAsync(
        OwnerKind ownerKind, Guid ownerId, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Media soft-deleted before <paramref name="cutoff"/> (for auto-purge).</summary>
    Task<IReadOnlyList<Models.Media>> ListDeletedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Models.Media>> ListStalePipelineAsync(DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken = default);

    Task<MediaStorageUsageSummary> GetStorageUsageAsync(
        OwnerKind ownerKind, Guid ownerId, CancellationToken cancellationToken = default);

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    Task AcquireStorageQuotaLockAsync(OwnerKind ownerKind, Guid ownerId, CancellationToken cancellationToken = default);

    Task<MediaUser?> GetMediaUserAsync(Guid mediaId, Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, bool>> GetFavoriteFlagsAsync(
        IEnumerable<Guid> mediaIds, Guid userId, CancellationToken cancellationToken = default);

    Task AddAsync(Models.Media media, CancellationToken cancellationToken = default);

    Task AddMediaUserAsync(MediaUser mediaUser, CancellationToken cancellationToken = default);

    Task EnqueueOutboxAsync(OutboxMessage message, CancellationToken cancellationToken = default);

    Task EnsurePendingOutboxAsync(OutboxMessage message, CancellationToken cancellationToken = default);

    void RemoveMediaUser(MediaUser mediaUser);

    void Remove(Models.Media media);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

internal sealed record MediaStorageUsageSummary(
    int MediaCount,
    long RawBytes,
    long CanonicalBytes,
    long ProxyBytes,
    long ThumbnailBytes,
    int TrashMediaCount,
    long TrashRawBytes,
    long TrashCanonicalBytes,
    long TrashProxyBytes,
    long TrashThumbnailBytes)
{
    public long StorageBytesUsed => RawBytes + CanonicalBytes + ProxyBytes + ThumbnailBytes;

    public long TrashBytesUsed => TrashRawBytes + TrashCanonicalBytes + TrashProxyBytes + TrashThumbnailBytes;
}
