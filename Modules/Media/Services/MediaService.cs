using Kuvox.Api.Modules.Auth.Contracts;
using Kuvox.Api.Modules.Media.Contracts;
using Kuvox.Api.Modules.Media.Dtos;
using Kuvox.Api.Modules.Media.Enums;
using Kuvox.Api.Modules.Media.Models;
using Kuvox.Api.Modules.Media.Repositories;
using Kuvox.Api.Modules.Shared.Dtos;
using Kuvox.Api.Modules.Shared.Infrastructure;
using Kuvox.Api.Modules.Shared.Infrastructure.RabbitMQ;
using MediatR;

namespace Kuvox.Api.Modules.Media.Services;

/// <summary>
/// Real Media business logic: workspace-scoped listing, "shared with me", sharing,
/// soft-delete → trash → restore → permanent delete. Mirrors <c>ProjectService</c>. Resolves
/// invitees through the Auth public contract (<see cref="IAuthApi"/>, Rule 2) and publishes
/// <see cref="MediaDeletedEvent"/> on permanent delete (Rule 4).
/// </summary>
internal sealed class MediaService(
    IMediaRepository media, 
    IAuthApi auth, 
    IMediator mediator,
    IFileStorageService storage,
    IRabbitMqPublisher publisher,
    ILogger<MediaService> logger)
    : IMediaService
{
    /// <summary>Trash auto-purge window (kept in sync with <c>TrashPurgeService</c>).</summary>
    public static readonly TimeSpan TrashRetention = TimeSpan.FromDays(7);

    public async Task<PagedResult<MediaDto>> ListByWorkspaceAsync(
        WorkspaceScope scope, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Normalize(page, pageSize);
        var (items, total) = await media.ListByWorkspaceAsync(OwnerKindOf(scope), scope.OwnerId, page, pageSize, cancellationToken);
        return new PagedResult<MediaDto>(items.Select(ToDto).ToList(), page, pageSize, total);
    }

    public async Task<PagedResult<MediaDto>> ListSharedWithMeAsync(
        Guid userId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Normalize(page, pageSize);
        var (items, total) = await media.ListSharedWithUserAsync(userId, page, pageSize, cancellationToken);
        return new PagedResult<MediaDto>(items.Select(x => ToDto(x.Media)).ToList(), page, pageSize, total);
    }

    public async Task<PagedResult<MediaTrashItemDto>> ListTrashAsync(
        WorkspaceScope scope, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Normalize(page, pageSize);
        var (items, total) = await media.ListTrashAsync(OwnerKindOf(scope), scope.OwnerId, page, pageSize, cancellationToken);
        return new PagedResult<MediaTrashItemDto>(items.Select(ToTrashDto).ToList(), page, pageSize, total);
    }

    public async Task<MediaDto> GetAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var item = await LoadLiveAsync(id, cancellationToken);
        if (!await CanAccessAsync(item, caller, cancellationToken))
        {
            throw DomainException.Forbidden("You do not have access to this media item.");
        }

        return ToDto(item);
    }

    public async Task<MediaDto> UploadRawAsync(
        WorkspaceScope scope, 
        CallerContext caller, 
        UploadMediaRequest request, 
        CancellationToken cancellationToken = default)
    {
        if (!scope.IsStudio && !await auth.UserExistsAsync(scope.OwnerId, cancellationToken))
        {
            throw DomainException.BadRequest("Unknown owner.");
        }

        if (scope.IsStudio && !caller.CanWriteStudioContent(scope.OwnerId))
        {
            throw DomainException.Forbidden("You do not have permission to create Studio media.");
        }

        if (request.File is null || request.File.Length == 0)
        {
            throw DomainException.BadRequest("File is empty.");
        }

        ValidateKindMatchesFile(request.Kind, request.File);

        var mediaId = Guid.NewGuid();

        StoredMediaObject? storedObject = null;

        try
        {
            storedObject = await storage.UploadRawAsync(
                request.File,
                mediaId,
                cancellationToken
            );

            Models.Media item = request.Kind switch
            {
                MediaKind.Video => new Video
                {
                    Id = mediaId,
                    DurationSeconds = 0,
                    Width = 0,
                    Height = 0,
                    FrameRate = 0,
                    OwnerId = scope.OwnerId,
                    OwnerKind = OwnerKindOf(scope),
                    Filename = Path.GetFileName(request.File.FileName).Trim(),
                    StorageKey = storedObject.ObjectKey,
                    RawBucketName = storedObject.BucketName,
                    RawStorageKey = storedObject.ObjectKey,
                    RawSizeBytes = storedObject.SizeBytes,
                    SizeBytes = storedObject.SizeBytes,
                    Status = MediaStatus.Uploaded
                },
                MediaKind.Audio => new Audio
                {
                    Id = mediaId,
                    DurationSeconds = 0,
                    OwnerId = scope.OwnerId,
                    OwnerKind = OwnerKindOf(scope),
                    Filename = Path.GetFileName(request.File.FileName).Trim(),
                    StorageKey = storedObject.ObjectKey,
                    RawBucketName = storedObject.BucketName,
                    RawStorageKey = storedObject.ObjectKey,
                    RawSizeBytes = storedObject.SizeBytes,
                    SizeBytes = storedObject.SizeBytes,
                    Status = MediaStatus.Uploaded
                },
                MediaKind.Image => new Photo
                {
                    Id = mediaId,
                    Width = 0,
                    Height = 0,
                    OwnerId = scope.OwnerId,
                    OwnerKind = OwnerKindOf(scope),
                    Filename = Path.GetFileName(request.File.FileName).Trim(),
                    StorageKey = storedObject.ObjectKey,
                    RawBucketName = storedObject.BucketName,
                    RawStorageKey = storedObject.ObjectKey,
                    RawSizeBytes = storedObject.SizeBytes,
                    SizeBytes = storedObject.SizeBytes,
                    Status = MediaStatus.Uploaded
                },
                _ => throw DomainException.BadRequest("Unsupported media kind.")
            };

            await media.AddAsync(item, cancellationToken);
            await media.SaveChangesAsync(cancellationToken);

            var optimizationEvent = new MediaOptimizationRequestedEvent(
                EventId: Guid.NewGuid(),
                EventType: "media.optimization.requested",
                OccurredAt: DateTimeOffset.UtcNow,
                MediaId: item.Id,
                UserId: caller.UserId,
                BucketName: storedObject.BucketName,
                ObjectKey: storedObject.ObjectKey,
                ContentType: storedObject.ContentType,
                OriginalFileName: item.Filename,
                SizeBytes: storedObject.SizeBytes,
                Kind: item.Kind
            );

            await publisher.PublishAsync(
                routingKey: "media.optimization.requested",
                message: optimizationEvent,
                cancellationToken
            );

            return ToDto(item);
        }
        catch
        {
            if (storedObject is not null)
            {
                await storage.DeleteAsync(
                    storedObject.BucketName,
                    storedObject.ObjectKey,
                    CancellationToken.None
                );
            }
            throw;
        }
    }

    public async Task HandleOptimizationCompletedAsync(
        MediaOptimizationCompletedEvent completed,
        CancellationToken cancellationToken = default)
    {
        var item = await media.GetByIdAsync(completed.MediaId, cancellationToken);
        if (item is null)
        {
            return;
        }

        if (item.Status == MediaStatus.Ready
            && item.CanonicalStorageKey == completed.Canonical?.ObjectKey)
        {
            return;
        }

        if (completed.Canonical is not { } canonical)
        {
            throw DomainException.BadRequest("Optimization completion missing canonical object.");
        }

        item.CanonicalBucketName = canonical.BucketName;
        item.CanonicalStorageKey = canonical.ObjectKey;
        item.CanonicalSizeBytes = canonical.SizeBytes;

        item.ProxyBucketName = completed.Proxy?.BucketName;
        item.ProxyStorageKey = completed.Proxy?.ObjectKey;
        item.ProxySizeBytes = completed.Proxy?.SizeBytes;

        item.ThumbnailBucketName = completed.Thumbnail?.BucketName;
        item.ThumbnailStorageKey = completed.Thumbnail?.ObjectKey;
        item.ThumbnailSizeBytes = completed.Thumbnail?.SizeBytes;

        item.StorageKey = canonical.ObjectKey;
        item.SizeBytes = canonical.SizeBytes;
        item.Status = MediaStatus.Processing;
        item.ErrorMessage = null;
        item.Codec = completed.Codec;
        item.UpdatedAt = DateTimeOffset.UtcNow;

        if (item.RawBucketName is null)
        {
            item.RawBucketName = completed.RawBucketName;
        }

        if (item.RawStorageKey is null)
        {
            item.RawStorageKey = completed.RawObjectKey;
        }

        item.RawSizeBytes = completed.RawSizeBytes;

        ApplyOptimizationMetadata(item, completed);

        await media.SaveChangesAsync(cancellationToken);

        if (item.RawBucketName is { Length: > 0 } rawBucket
            && item.RawStorageKey is { Length: > 0 } rawKey)
        {
            try
            {
                await storage.DeleteAsync(rawBucket, rawKey, cancellationToken);
                item.RawBucketName = null;
                item.RawStorageKey = null;
                item.RawSizeBytes = null;
                item.UpdatedAt = DateTimeOffset.UtcNow;
                await media.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "[Media] Failed to delete raw object {BucketName}/{ObjectKey} after optimizing media {MediaId}.",
                    rawBucket,
                    rawKey,
                    item.Id);
            }
        }

        var ingestionEvent = new IngestionRequestedEvent(
            EventId: Guid.NewGuid(),
            EventType: "ingestion.requested",
            OccurredAt: DateTimeOffset.UtcNow,
            MediaId: item.Id,
            OwnerId: item.OwnerId,
            OwnerKind: item.OwnerKind,
            Kind: item.Kind,
            Canonical: canonical,
            Proxy: completed.Proxy,
            Thumbnail: completed.Thumbnail,
            DurationSeconds: completed.DurationSeconds,
            Width: completed.Width,
            Height: completed.Height,
            FrameRate: completed.FrameRate,
            Codec: completed.Codec
        );

        await publisher.PublishAsync(
            routingKey: "ingestion.requested",
            message: ingestionEvent,
            cancellationToken: cancellationToken
        );
    }

    public async Task HandleOptimizationFailedAsync(
        MediaOptimizationFailedEvent failed,
        CancellationToken cancellationToken = default)
    {
        var item = await media.GetByIdAsync(failed.MediaId, cancellationToken);
        if (item is null)
        {
            return;
        }

        item.Status = MediaStatus.Failed;
        item.ErrorMessage = string.IsNullOrWhiteSpace(failed.ErrorMessage)
            ? failed.ErrorCode
            : failed.ErrorMessage;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await media.SaveChangesAsync(cancellationToken);
    }

    public async Task HandleIngestionCompletedAsync(
        IngestionCompletedEvent completed,
        CancellationToken cancellationToken = default)
    {
        var item = await media.GetByIdAsync(completed.MediaId, cancellationToken);
        if (item is null)
        {
            return;
        }

        if (item.Status == MediaStatus.Ready)
        {
            return;
        }

        item.Status = MediaStatus.Ready;
        item.ErrorMessage = null;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await media.SaveChangesAsync(cancellationToken);
    }

    public async Task HandleIngestionFailedAsync(
        IngestionFailedEvent failed,
        CancellationToken cancellationToken = default)
    {
        var item = await media.GetByIdAsync(failed.MediaId, cancellationToken);
        if (item is null)
        {
            return;
        }

        item.Status = MediaStatus.Failed;
        item.ErrorMessage = string.IsNullOrWhiteSpace(failed.ErrorMessage)
            ? failed.ErrorCode
            : failed.ErrorMessage;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await media.SaveChangesAsync(cancellationToken);
    }

    public async Task ShareAsync(
        Guid id, CallerContext caller, ShareMediaRequest request, CancellationToken cancellationToken = default)
    {
        var item = await LoadLiveAsync(id, cancellationToken);
        RequireWrite(item, caller);

        var invitee = await auth.GetSummaryByEmailAsync(request.Email.Trim().ToLowerInvariant(), cancellationToken)
            ?? throw DomainException.NotFound("No user with that email.");

        if (item.OwnerKind == OwnerKind.User && invitee.Id == item.OwnerId)
        {
            throw DomainException.Conflict("The owner already has access.");
        }

        var existing = await media.GetMediaUserAsync(item.Id, invitee.Id, cancellationToken);
        if (existing is null)
        {
            MediaUser newUser = item switch
            {
                Video => new VideoUser { MediaId = item.Id, UserId = invitee.Id, Role = request.Role, IsFavorite = false },
                Audio => new AudioUser { MediaId = item.Id, UserId = invitee.Id, Role = request.Role, IsFavorite = false },
                Photo => new PhotoUser { MediaId = item.Id, UserId = invitee.Id, Role = request.Role, IsFavorite = false },
                _ => throw new NotSupportedException()
            };
            await media.AddMediaUserAsync(newUser, cancellationToken);
        }
        else
        {
            existing.Role = request.Role;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await media.SaveChangesAsync(cancellationToken);
    }

    public async Task UnshareAsync(Guid id, CallerContext caller, Guid userId, CancellationToken cancellationToken = default)
    {
        var item = await LoadLiveAsync(id, cancellationToken);
        RequireWrite(item, caller);

        var share = await media.GetMediaUserAsync(item.Id, userId, cancellationToken);
        if (share is not null)
        {
            media.RemoveMediaUser(share);
            await media.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task SoftDeleteAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var item = await LoadLiveAsync(id, cancellationToken);
        RequireWrite(item, caller);

        item.DeletedAt = DateTimeOffset.UtcNow;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await media.SaveChangesAsync(cancellationToken);
    }

    public async Task RestoreAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var item = await media.GetByIdAsync(id, cancellationToken)
            ?? throw DomainException.NotFound("Media not found.");
        RequireTrashManage(item, caller);

        item.DeletedAt = null;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await media.SaveChangesAsync(cancellationToken);
    }

    public async Task PermanentDeleteAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var item = await media.GetByIdAsync(id, cancellationToken)
            ?? throw DomainException.NotFound("Media not found.");
        RequireTrashManage(item, caller);

        media.Remove(item);
        await media.SaveChangesAsync(cancellationToken);

        foreach (var storedObject in StoredObjectsFor(item))
        {
            try
            {
                await storage.DeleteAsync(storedObject.BucketName, storedObject.ObjectKey, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "[Media] Failed to delete object {BucketName}/{ObjectKey} for media {MediaId}.",
                    storedObject.BucketName,
                    storedObject.ObjectKey,
                    item.Id);
            }
        }

        await mediator.Publish(new MediaDeletedEvent(item.Id), cancellationToken);
    }

    private static void ValidateKindMatchesFile(MediaKind kind, IFormFile file)
    {
        var contentType = file.ContentType.ToLowerInvariant();

        var valid = kind switch
        {
            MediaKind.Video => contentType.StartsWith("video/"),
            MediaKind.Audio => contentType.StartsWith("audio/"),
            MediaKind.Image => contentType.StartsWith("image/"),
            _ => false
        };

        if (!valid)
        {
            throw DomainException.BadRequest($"File content type '{file.ContentType}' does not match media kind '{kind}'.");
        }
    }

    private static void ApplyOptimizationMetadata(
        Models.Media item,
        MediaOptimizationCompletedEvent completed)
    {
        switch (item)
        {
            case Video video:
                video.DurationSeconds = completed.DurationSeconds ?? video.DurationSeconds;
                video.Width = completed.Width ?? video.Width;
                video.Height = completed.Height ?? video.Height;
                video.FrameRate = completed.FrameRate ?? video.FrameRate;
                break;
            case Audio audio:
                audio.DurationSeconds = completed.DurationSeconds ?? audio.DurationSeconds;
                break;
            case Photo photo:
                photo.Width = completed.Width ?? photo.Width;
                photo.Height = completed.Height ?? photo.Height;
                break;
        }
    }

    private static IEnumerable<(string BucketName, string ObjectKey)> StoredObjectsFor(Models.Media item)
    {
        if (item.RawBucketName is { Length: > 0 } rawBucket
            && item.RawStorageKey is { Length: > 0 } rawKey)
        {
            yield return (rawBucket, rawKey);
        }

        if (item.CanonicalBucketName is { Length: > 0 } canonicalBucket
            && item.CanonicalStorageKey is { Length: > 0 } canonicalKey)
        {
            yield return (canonicalBucket, canonicalKey);
        }

        if (item.ProxyBucketName is { Length: > 0 } proxyBucket
            && item.ProxyStorageKey is { Length: > 0 } proxyKey)
        {
            yield return (proxyBucket, proxyKey);
        }

        if (item.ThumbnailBucketName is { Length: > 0 } thumbnailBucket
            && item.ThumbnailStorageKey is { Length: > 0 } thumbnailKey)
        {
            yield return (thumbnailBucket, thumbnailKey);
        }
    }

    private async Task<Models.Media> LoadLiveAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await media.GetByIdAsync(id, cancellationToken);
        return item is null || item.DeletedAt is not null
            ? throw DomainException.NotFound("Media not found.")
            : item;
    }

    private static bool CanRead(Models.Media item, CallerContext caller) =>
        item.OwnerKind == OwnerKind.User
            ? caller.OwnsAsUser(item.OwnerId)
            : caller.InStudio(item.OwnerId);

    private static bool CanWrite(Models.Media item, CallerContext caller) =>
        item.OwnerKind == OwnerKind.User
            ? caller.OwnsAsUser(item.OwnerId)
            : caller.CanWriteStudioContent(item.OwnerId);

    private static bool CanManageTrash(Models.Media item, CallerContext caller) =>
        item.OwnerKind == OwnerKind.User
            ? caller.OwnsAsUser(item.OwnerId)
            : caller.CanManageStudioAccess(item.OwnerId);

    private static void RequireWrite(Models.Media item, CallerContext caller)
    {
        if (!CanWrite(item, caller))
        {
            throw DomainException.Forbidden("You do not have permission to modify this media item.");
        }
    }

    private static void RequireTrashManage(Models.Media item, CallerContext caller)
    {
        if (!CanManageTrash(item, caller))
        {
            throw DomainException.Forbidden("You do not have permission to manage Studio trash.");
        }
    }

    private async Task<bool> CanAccessAsync(Models.Media item, CallerContext caller, CancellationToken cancellationToken)
    {
        if (CanRead(item, caller))
        {
            return true;
        }

        return await media.GetMediaUserAsync(item.Id, caller.UserId, cancellationToken) is not null;
    }

    private static OwnerKind OwnerKindOf(WorkspaceScope scope) => scope.IsStudio ? OwnerKind.Studio : OwnerKind.User;

    private static (int Page, int PageSize) Normalize(int page, int pageSize) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, 100));

    internal static MediaDto ToDto(Models.Media m)
    {
        double? duration = m switch { Video v => v.DurationSeconds, Audio a => a.DurationSeconds, _ => null };
        int? width = m switch { Video v => v.Width, Photo p => p.Width, _ => null };
        int? height = m switch { Video v => v.Height, Photo p => p.Height, _ => null };
        double? frameRate = m switch { Video v => v.FrameRate, _ => null };
        
        return new(
            m.Id,
            m.OwnerId,
            m.OwnerKind,
            m.Kind,
            m.Filename,
            m.StorageKey,
            m.SizeBytes,
            m.Status.ToString(),
            m.CanonicalStorageKey,
            m.ProxyStorageKey,
            m.ThumbnailStorageKey,
            m.ErrorMessage,
            duration,
            width,
            height,
            m.Codec,
            frameRate,
            m.CreatedAt);
    }

    private static MediaTrashItemDto ToTrashDto(Models.Media m)
    {
        var deletedAt = m.DeletedAt ?? DateTimeOffset.UtcNow;
        var remaining = (deletedAt + TrashRetention) - DateTimeOffset.UtcNow;
        var purgesInDays = Math.Max(0, (int)Math.Ceiling(remaining.TotalDays));
        return new MediaTrashItemDto(m.Id, m.Kind, m.Filename, deletedAt, purgesInDays);
    }
}
