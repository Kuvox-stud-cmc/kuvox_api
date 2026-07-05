using Kuvox.Api.Modules.Auth.Contracts;
using Kuvox.Api.Modules.Media.Contracts;
using Kuvox.Api.Modules.Media.Dtos;
using Kuvox.Api.Modules.Media.Enums;
using Kuvox.Api.Modules.Media.Models;
using Kuvox.Api.Modules.Media.Repositories;
using Kuvox.Api.Modules.Notifications;
using Kuvox.Api.Modules.Shared.Dtos;
using Kuvox.Api.Modules.Shared.Infrastructure;
using Kuvox.Api.Modules.Shared.Infrastructure.Messaging;
using Kuvox.Api.Modules.Shared.Infrastructure.RabbitMQ;
using MediatR;
using Microsoft.Extensions.Options;

namespace Kuvox.Api.Modules.Media.Services;

/// <summary>
/// Real Media business logic: workspace-scoped listing, "shared with me", sharing,
/// soft-delete → trash → restore → permanent delete. Mirrors <c>ProjectService</c>. Resolves
/// invitees through the Auth public contract (<see cref="IAuthApi"/>, Rule 2) and publishes
/// <see cref="MediaDeletedEvent"/> on permanent delete (Rule 4).
/// </summary>
internal sealed class MediaService(
    IMediaRepository media, 
    IAlbumRepository albums,
    IAuthApi auth, 
    INotificationsApi notifications,
    IMediator mediator,
    IFileStorageService storage,
    IMediaRealtimeNotifier realtime,
    IOptions<RabbitMqOptions> rabbitMqOptions,
    ILogger<MediaService> logger)
    : IMediaService
{
    /// <summary>Trash auto-purge window (kept in sync with <c>TrashPurgeService</c>).</summary>
    public static readonly TimeSpan TrashRetention = TimeSpan.FromDays(7);
    private const long StudioStorageQuotaBytes = 500L * 1024L * 1024L * 1024L;

    public async Task<PagedResult<MediaDto>> ListByWorkspaceAsync(
        WorkspaceScope scope, CallerContext caller, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Normalize(page, pageSize);
        var (items, total) = await media.ListByWorkspaceAsync(OwnerKindOf(scope), scope.OwnerId, page, pageSize, cancellationToken);
        if (scope.IsStudio && !caller.IsStudioOwner(scope.OwnerId))
        {
            items = await FilterVisibleAsync(items, caller, cancellationToken);
            total = items.Count;
        }
        var flags = await media.GetFavoriteFlagsAsync(items.Select(item => item.Id), caller.UserId, cancellationToken);
        return new PagedResult<MediaDto>(items.Select(item => ToDto(item, flags.GetValueOrDefault(item.Id))).ToList(), page, pageSize, total);
    }

    public async Task<PagedResult<MediaDto>> ListSharedWithMeAsync(
        Guid userId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Normalize(page, pageSize);
        var (items, total) = await media.ListSharedWithUserAsync(userId, page, pageSize, cancellationToken);
        var flags = await media.GetFavoriteFlagsAsync(items.Select(item => item.Media.Id), userId, cancellationToken);
        var owners = await GetUserOwnerSummariesAsync(items.Select(item => item.Media), cancellationToken);
        return new PagedResult<MediaDto>(
            items.Select(item => ToDto(
                item.Media,
                flags.GetValueOrDefault(item.Media.Id),
                owners.GetValueOrDefault(item.Media.OwnerId))).ToList(),
            page,
            pageSize,
            total);
    }

    public async Task<PagedResult<MediaTrashItemDto>> ListTrashAsync(
        WorkspaceScope scope, CallerContext caller, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Normalize(page, pageSize);
        var (items, total) = await media.ListTrashAsync(OwnerKindOf(scope), scope.OwnerId, page, pageSize, cancellationToken);
        if (scope.IsStudio && !caller.IsStudioOwner(scope.OwnerId))
        {
            items = await FilterVisibleAsync(items, caller, cancellationToken);
            total = items.Count;
        }
        return new PagedResult<MediaTrashItemDto>(items.Select(ToTrashDto).ToList(), page, pageSize, total);
    }

    public async Task<MediaDto> GetAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var item = await LoadLiveAsync(id, cancellationToken);
        if (!await CanAccessAsync(item, caller, cancellationToken))
        {
            throw DomainException.Forbidden("You do not have access to this media item.");
        }

        var mediaUser = await media.GetMediaUserAsync(item.Id, caller.UserId, cancellationToken);
        return ToDto(item, mediaUser?.IsFavorite ?? false);
    }

    public async Task<MediaObjectDownload> GetObjectAsync(
        Guid id,
        string variant,
        CallerContext caller,
        CancellationToken cancellationToken = default)
    {
        var item = await LoadLiveAsync(id, cancellationToken);
        if (!await CanAccessAsync(item, caller, cancellationToken))
        {
            throw DomainException.Forbidden("You do not have access to this media item.");
        }

        var normalizedVariant = variant.Trim().ToLowerInvariant();
        var storedObject = StoredObjectForVariant(item, normalizedVariant);
        logger.LogInformation(
            "[Media] Object request media {MediaId}, variant {Variant}, kind {Kind}, hasThumbnail {HasThumbnail}, hasCanonical {HasCanonical}, hasProxy {HasProxy}, hasRaw {HasRaw}, resolved {Resolved}.",
            item.Id,
            normalizedVariant,
            item.Kind,
            HasStoredObject(item.ThumbnailBucketName, item.ThumbnailStorageKey),
            HasStoredObject(item.CanonicalBucketName, item.CanonicalStorageKey),
            HasStoredObject(item.ProxyBucketName, item.ProxyStorageKey),
            HasStoredObject(item.RawBucketName, item.RawStorageKey),
            storedObject is not null);

        if (storedObject is null)
        {
            logger.LogWarning(
                "[Media] Object not found for media {MediaId}, variant {Variant}; no stored key resolved.",
                item.Id,
                normalizedVariant);
            throw DomainException.NotFound("Media object not found.");
        }

        var (bucketName, objectKey) = storedObject.Value;

        try
        {
            var downloaded = await storage.DownloadAsync(bucketName, objectKey, cancellationToken);
            return new MediaObjectDownload(
                downloaded.Stream,
                NormalizeContentType(downloaded.ContentType, objectKey, item.Kind),
                downloaded.ContentLength,
                downloaded.ETag,
                DownloadFileName(item.Filename, objectKey));
        }
        catch (Amazon.S3.AmazonS3Exception ex) when ((int)ex.StatusCode == StatusCodes.Status404NotFound
            || string.Equals(ex.ErrorCode, "NoSuchKey", StringComparison.OrdinalIgnoreCase))
        {
            if (normalizedVariant == "thumbnail"
                && item.Kind == MediaKind.Image
                && IsThumbnailObject(item, bucketName, objectKey)
                && CanonicalObject(item) is { } canonicalObject)
            {
                logger.LogWarning(
                    ex,
                    "[Media] Thumbnail object missing for image media {MediaId}, bucket {BucketName}, key {ObjectKey}; falling back to canonical object.",
                    item.Id,
                    bucketName,
                    objectKey);

                try
                {
                    var fallback = await storage.DownloadAsync(
                        canonicalObject.BucketName,
                        canonicalObject.ObjectKey,
                        cancellationToken);
                    return new MediaObjectDownload(
                        fallback.Stream,
                        NormalizeContentType(fallback.ContentType, canonicalObject.ObjectKey, item.Kind),
                        fallback.ContentLength,
                        fallback.ETag,
                        DownloadFileName(item.Filename, canonicalObject.ObjectKey));
                }
                catch (Amazon.S3.AmazonS3Exception fallbackEx) when ((int)fallbackEx.StatusCode == StatusCodes.Status404NotFound
                    || string.Equals(fallbackEx.ErrorCode, "NoSuchKey", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning(
                        fallbackEx,
                        "[Media] Canonical fallback object missing for image media {MediaId}, bucket {BucketName}, key {ObjectKey}.",
                        item.Id,
                        canonicalObject.BucketName,
                        canonicalObject.ObjectKey);
                }
            }

            logger.LogWarning(
                ex,
                "[Media] Storage object missing for media {MediaId}, variant {Variant}, bucket {BucketName}, key {ObjectKey}.",
                item.Id,
                normalizedVariant,
                bucketName,
                objectKey);
            throw DomainException.NotFound("Media object not found.");
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "[Media] Storage download failed for media {MediaId}, variant {Variant}, bucket {BucketName}, key {ObjectKey}.",
                item.Id,
                normalizedVariant,
                bucketName,
                objectKey);
            throw;
        }
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
        var filename = NormalizeUploadFilename(request.Filename, request.File.FileName);
        var ownerKind = OwnerKindOf(scope);

        StoredMediaObject? storedObject = null;

        try
        {
            await using var quotaTransaction = await media.BeginTransactionAsync(cancellationToken);
            await media.AcquireStorageQuotaLockAsync(ownerKind, scope.OwnerId, cancellationToken);

            var storagePlan = await ResolveStoragePlanAsync(scope, cancellationToken);
            var usage = await media.GetStorageUsageAsync(ownerKind, scope.OwnerId, cancellationToken);
            if (usage.StorageBytesUsed + request.File.Length > storagePlan.StorageBytes)
            {
                throw DomainException.PayloadTooLarge("Storage quota exceeded.");
            }

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
                    OwnerKind = ownerKind,
                    Filename = filename,
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
                    OwnerKind = ownerKind,
                    Filename = filename,
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
                    OwnerKind = ownerKind,
                    Filename = filename,
                    StorageKey = storedObject.ObjectKey,
                    RawBucketName = storedObject.BucketName,
                    RawStorageKey = storedObject.ObjectKey,
                    RawSizeBytes = storedObject.SizeBytes,
                    SizeBytes = storedObject.SizeBytes,
                    Status = MediaStatus.Uploaded
                },
                _ => throw DomainException.BadRequest("Unsupported media kind.")
            };

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

            await media.AddAsync(item, cancellationToken);
            await media.EnqueueOutboxAsync(
                OutboxMessage.Create(
                    dedupeKey: $"media.optimization.requested:{item.Id}",
                    exchange: rabbitMqOptions.Value.ExchangeName,
                    routingKey: "media.optimization.requested",
                    eventType: optimizationEvent.EventType,
                    payload: optimizationEvent),
                cancellationToken);
            await media.SaveChangesAsync(cancellationToken);
            await quotaTransaction.CommitAsync(cancellationToken);

            var dto = ToDto(item);
            await realtime.MediaUpdatedAsync(item, dto, "uploaded", cancellationToken);

            return dto;
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

    public async Task<MediaStorageUsageDto> GetPersonalStorageUsageAsync(
        CallerContext caller,
        CancellationToken cancellationToken = default)
    {
        return await GetStorageUsageAsync(
            new WorkspaceScope(IsStudio: false, OwnerId: caller.UserId),
            cancellationToken);
    }

    public async Task<MediaStorageUsageDto> GetStorageUsageAsync(
        WorkspaceScope scope,
        CancellationToken cancellationToken = default)
    {
        var plan = await ResolveStoragePlanAsync(scope, cancellationToken);
        var usage = await media.GetStorageUsageAsync(OwnerKindOf(scope), scope.OwnerId, cancellationToken);
        return ToStorageUsageDto(plan.Plan, plan.StorageBytes, usage);
    }

    public async Task<MediaDto> SetFavoriteAsync(
        Guid id,
        CallerContext caller,
        ToggleMediaFavoriteRequest request,
        CancellationToken cancellationToken = default)
    {
        var item = await LoadLiveAsync(id, cancellationToken);
        if (!await CanAccessAsync(item, caller, cancellationToken))
        {
            throw DomainException.Forbidden("You do not have access to this media item.");
        }

        var mediaUser = await media.GetMediaUserAsync(item.Id, caller.UserId, cancellationToken);
        if (mediaUser is null)
        {
            if (request.IsFavorite && item.OwnerKind == OwnerKind.User && caller.OwnsAsUser(item.OwnerId))
            {
                mediaUser = CreateMediaUser(item, caller.UserId, Permission.Owner);
                mediaUser.IsFavorite = true;
                await media.AddMediaUserAsync(mediaUser, cancellationToken);
                await media.SaveChangesAsync(cancellationToken);
            }

            return ToDto(item, mediaUser?.IsFavorite ?? false);
        }

        if (mediaUser.IsFavorite != request.IsFavorite)
        {
            mediaUser.IsFavorite = request.IsFavorite;
            mediaUser.UpdatedAt = DateTimeOffset.UtcNow;
            await media.SaveChangesAsync(cancellationToken);
        }

        return ToDto(item, mediaUser.IsFavorite);
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

        if (completed.Canonical is not { } canonical)
        {
            throw DomainException.BadRequest("Optimization completion missing canonical object.");
        }

        if (item.Status == MediaStatus.Ready)
        {
            if (item.CanonicalStorageKey != canonical.ObjectKey)
            {
                logger.LogWarning(
                    "[Media] Ignoring optimization completion for already-ready media {MediaId} with canonical object {CanonicalObjectKey}.",
                    item.Id,
                    canonical.ObjectKey);
            }

            return;
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
        item.Status = item.Kind == MediaKind.Video
            ? MediaStatus.Processing
            : MediaStatus.Ready;
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

        if (item.Kind == MediaKind.Video)
        {
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

            await media.EnqueueOutboxAsync(
                OutboxMessage.Create(
                    dedupeKey: $"ingestion.requested:{item.Id}",
                    exchange: rabbitMqOptions.Value.ExchangeName,
                    routingKey: "ingestion.requested",
                    eventType: ingestionEvent.EventType,
                    payload: ingestionEvent),
                cancellationToken);
        }

        await media.SaveChangesAsync(cancellationToken);
        await realtime.MediaUpdatedAsync(
            item,
            ToDto(item),
            item.Kind == MediaKind.Video ? "processing" : "ready",
            cancellationToken);
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

        if (item.Status is MediaStatus.Processing or MediaStatus.Ready)
        {
            return;
        }

        item.Status = MediaStatus.Failed;
        item.ErrorMessage = string.IsNullOrWhiteSpace(failed.ErrorMessage)
            ? failed.ErrorCode
            : failed.ErrorMessage;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await media.SaveChangesAsync(cancellationToken);
        await realtime.MediaUpdatedAsync(
            item,
            ToDto(item),
            "failed",
            cancellationToken,
            errorCode: failed.ErrorCode,
            errorMessage: item.ErrorMessage);
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
        await realtime.MediaUpdatedAsync(
            item,
            ToDto(item),
            "ready",
            cancellationToken,
            shotCount: completed.ShotCount);
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

        if (item.Status == MediaStatus.Ready)
        {
            return;
        }

        item.Status = MediaStatus.Failed;
        item.ErrorMessage = string.IsNullOrWhiteSpace(failed.ErrorMessage)
            ? failed.ErrorCode
            : failed.ErrorMessage;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await media.SaveChangesAsync(cancellationToken);
        await realtime.MediaUpdatedAsync(
            item,
            ToDto(item),
            "failed",
            cancellationToken,
            errorCode: failed.ErrorCode,
            errorMessage: item.ErrorMessage);
    }

    public async Task ShareAsync(
        Guid id, CallerContext caller, ShareMediaRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Role == Permission.Owner)
        {
            throw DomainException.BadRequest("Choose Viewer or Editor access.");
        }

        var item = await LoadLiveAsync(id, cancellationToken);
        await RequireWriteAsync(item, caller, cancellationToken);

        var invitee = await auth.GetSummaryByEmailAsync(request.Email.Trim().ToLowerInvariant(), cancellationToken)
            ?? throw DomainException.NotFound("No user with that email.");

        if (item.OwnerKind == OwnerKind.User && invitee.Id == item.OwnerId)
        {
            throw DomainException.Conflict("The owner already has access.");
        }

        var existing = await media.GetMediaUserAsync(item.Id, invitee.Id, cancellationToken);
        if (existing is null)
        {
            MediaUser newUser = CreateMediaUser(item, invitee.Id, request.Role);
            await media.AddMediaUserAsync(newUser, cancellationToken);
        }
        else
        {
            existing.Role = request.Role;
            existing.IsHidden = false;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await media.SaveChangesAsync(cancellationToken);
        await notifications.CreateAsync(
            invitee.Id,
            null,
            "MediaAccessChanged",
            $"Media was shared with you: {item.Filename}.",
            "/dashboard/shared-assets",
            cancellationToken);
    }

    public async Task UnshareAsync(Guid id, CallerContext caller, Guid userId, CancellationToken cancellationToken = default)
    {
        var item = await LoadLiveAsync(id, cancellationToken);
        await RequireWriteAsync(item, caller, cancellationToken);

        var share = await media.GetMediaUserAsync(item.Id, userId, cancellationToken);
        if (share is not null)
        {
            media.RemoveMediaUser(share);
            await media.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<MediaAccessMemberDto>> ListAccessAsync(
        Guid id,
        CallerContext caller,
        CancellationToken cancellationToken = default)
    {
        var item = await LoadLiveAsync(id, cancellationToken);
        RequireStudioAccessManage(item, caller);
        return await BuildAccessRowsAsync(item, caller, cancellationToken);
    }

    public async Task<IReadOnlyList<MediaAccessMemberDto>> UpdateAccessAsync(
        Guid id,
        CallerContext caller,
        UpdateMediaAccessRequest request,
        CancellationToken cancellationToken = default)
    {
        var item = await LoadLiveAsync(id, cancellationToken);
        RequireStudioAccessManage(item, caller);
        var target = await auth.GetStudioMemberAsync(item.OwnerId, request.UserId, cancellationToken)
            ?? throw DomainException.NotFound("Studio member not found.");

        RequireCanManageTarget(caller, item.OwnerId, target.Role);
        var role = request.Role ?? DefaultPermissionForStudioRole(target.Role);
        if (role == Permission.Owner)
        {
            throw DomainException.BadRequest("Choose Viewer or Editor access.");
        }

        var access = await media.GetMediaUserAsync(item.Id, request.UserId, cancellationToken);
        if (access is null)
        {
            access = CreateMediaUser(item, request.UserId, role);
            access.IsHidden = request.IsHidden;
            await media.AddMediaUserAsync(access, cancellationToken);
        }
        else
        {
            access.Role = role;
            access.IsHidden = request.IsHidden;
            access.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await media.SaveChangesAsync(cancellationToken);
        return await BuildAccessRowsAsync(item, caller, cancellationToken);
    }

    public async Task SoftDeleteAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var item = await LoadLiveAsync(id, cancellationToken);
        await RequireWriteAsync(item, caller, cancellationToken);

        item.DeletedAt = DateTimeOffset.UtcNow;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await media.SaveChangesAsync(cancellationToken);
    }

    public async Task RestoreAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var item = await media.GetByIdAsync(id, cancellationToken)
            ?? throw DomainException.NotFound("Media not found.");
        await RequireTrashManageAsync(item, caller, cancellationToken);

        item.DeletedAt = null;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await media.SaveChangesAsync(cancellationToken);
    }

    public async Task PermanentDeleteAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var item = await media.GetByIdAsync(id, cancellationToken)
            ?? throw DomainException.NotFound("Media not found.");
        await RequireTrashManageAsync(item, caller, cancellationToken);

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

    private static string NormalizeUploadFilename(string? requestedFilename, string uploadedFilename)
    {
        var fallback = SafeFileName(uploadedFilename);
        var candidate = string.IsNullOrWhiteSpace(requestedFilename)
            ? fallback
            : SafeFileName(requestedFilename);

        if (string.IsNullOrWhiteSpace(candidate))
        {
            throw DomainException.BadRequest("Filename is required.");
        }

        if (string.IsNullOrWhiteSpace(Path.GetExtension(candidate)))
        {
            candidate += Path.GetExtension(fallback);
        }

        return candidate.Length <= 512 ? candidate : candidate[..512];
    }

    private static string SafeFileName(string value)
    {
        var name = Path.GetFileName(value).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return name;
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

    private static (string BucketName, string ObjectKey)? StoredObjectForVariant(Models.Media item, string variant)
    {
        return variant switch
        {
            "thumbnail" when item.ThumbnailBucketName is { Length: > 0 } thumbnailBucket
                && item.ThumbnailStorageKey is { Length: > 0 } thumbnailKey
                => (thumbnailBucket, thumbnailKey),
            "thumbnail" when item.Kind == MediaKind.Image
                && item.CanonicalBucketName is { Length: > 0 } canonicalBucket
                && item.CanonicalStorageKey is { Length: > 0 } canonicalKey
                => (canonicalBucket, canonicalKey),
            "canonical" when item.CanonicalBucketName is { Length: > 0 } canonicalBucket
                && item.CanonicalStorageKey is { Length: > 0 } canonicalKey
                => (canonicalBucket, canonicalKey),
            "proxy" when item.ProxyBucketName is { Length: > 0 } proxyBucket
                && item.ProxyStorageKey is { Length: > 0 } proxyKey
                => (proxyBucket, proxyKey),
            "raw" when item.RawBucketName is { Length: > 0 } rawBucket
                && item.RawStorageKey is { Length: > 0 } rawKey
                => (rawBucket, rawKey),
            _ => null
        };
    }

    private static bool HasStoredObject(string? bucketName, string? objectKey) =>
        bucketName is { Length: > 0 } && objectKey is { Length: > 0 };

    private static bool IsThumbnailObject(Models.Media item, string bucketName, string objectKey) =>
        string.Equals(item.ThumbnailBucketName, bucketName, StringComparison.Ordinal)
        && string.Equals(item.ThumbnailStorageKey, objectKey, StringComparison.Ordinal);

    private static (string BucketName, string ObjectKey)? CanonicalObject(Models.Media item) =>
        item.CanonicalBucketName is { Length: > 0 } canonicalBucket
            && item.CanonicalStorageKey is { Length: > 0 } canonicalKey
            ? (canonicalBucket, canonicalKey)
            : null;

    private static string NormalizeContentType(string? storedContentType, string objectKey, MediaKind kind)
    {
        if (!string.IsNullOrWhiteSpace(storedContentType)
            && !string.Equals(storedContentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return storedContentType;
        }

        return ContentTypeForObject(objectKey, kind);
    }

    private static string ContentTypeForObject(string objectKey, MediaKind kind)
    {
        return Path.GetExtension(objectKey).ToLowerInvariant() switch
        {
            ".webp" => "image/webp",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".mov" => "video/quicktime",
            ".mp3" => "audio/mpeg",
            ".m4a" => "audio/mp4",
            ".opus" => "audio/opus",
            ".wav" => "audio/wav",
            ".ogg" => "audio/ogg",
            _ => kind switch
            {
                MediaKind.Image => "image/webp",
                MediaKind.Video => "video/mp4",
                MediaKind.Audio => "audio/mpeg",
                _ => "application/octet-stream"
            }
        };
    }

    private static string DownloadFileName(string filename, string objectKey)
    {
        var baseName = Path.GetFileNameWithoutExtension(SafeFileName(filename));
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "media";
        }

        var extension = Path.GetExtension(objectKey);
        return $"{baseName}{extension}";
    }

    private async Task<Models.Media> LoadLiveAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await media.GetByIdAsync(id, cancellationToken);
        return item is null || item.DeletedAt is not null
            ? throw DomainException.NotFound("Media not found.")
            : item;
    }

    private async Task<(string Plan, long StorageBytes)> ResolveStoragePlanAsync(
        WorkspaceScope scope,
        CancellationToken cancellationToken)
    {
        if (scope.IsStudio)
        {
            return ("Studio", StudioStorageQuotaBytes);
        }

        return await ResolveUserStoragePlanAsync(scope.OwnerId, cancellationToken);
    }

    private async Task<(string Plan, long StorageBytes)> ResolveUserStoragePlanAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var plan = await auth.GetPlanLimitsAsync(userId, cancellationToken)
            ?? throw DomainException.BadRequest("Unknown owner.");
        return (plan.Plan, plan.StorageBytes);
    }

    private static MediaStorageUsageDto ToStorageUsageDto(
        string plan,
        long quotaBytes,
        MediaStorageUsageSummary usage)
    {
        var used = usage.StorageBytesUsed;
        var percent = quotaBytes <= 0
            ? 0
            : Math.Min(100, Math.Round((double)used / quotaBytes * 100, 1));

        return new MediaStorageUsageDto(
            plan,
            used,
            quotaBytes,
            percent,
            usage.MediaCount,
            used - usage.TrashBytesUsed,
            usage.TrashBytesUsed,
            new MediaStorageObjectBreakdownDto(
                usage.RawBytes,
                usage.CanonicalBytes,
                usage.ProxyBytes,
                usage.ThumbnailBytes),
            new MediaStorageObjectBreakdownDto(
                usage.TrashRawBytes,
                usage.TrashCanonicalBytes,
                usage.TrashProxyBytes,
                usage.TrashThumbnailBytes));
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

    private async Task RequireWriteAsync(Models.Media item, CallerContext caller, CancellationToken cancellationToken)
    {
        if (!await CanWriteAsync(item, caller, cancellationToken))
        {
            throw DomainException.Forbidden("You do not have permission to modify this media item.");
        }
    }

    private async Task RequireTrashManageAsync(Models.Media item, CallerContext caller, CancellationToken cancellationToken)
    {
        if (item.OwnerKind == OwnerKind.Studio && !await CanAccessAsync(item, caller, cancellationToken))
        {
            throw DomainException.Forbidden("You do not have access to this media item.");
        }

        if (!CanManageTrash(item, caller))
        {
            throw DomainException.Forbidden("You do not have permission to manage Studio trash.");
        }
    }

    private async Task<bool> CanWriteAsync(Models.Media item, CallerContext caller, CancellationToken cancellationToken)
    {
        if (item.OwnerKind == OwnerKind.User)
        {
            if (caller.OwnsAsUser(item.OwnerId))
            {
                return true;
            }

            var share = await media.GetMediaUserAsync(item.Id, caller.UserId, cancellationToken);
            return share is { IsHidden: false, Role: Permission.Owner or Permission.Editor };
        }

        if (caller.IsStudioOwner(item.OwnerId))
        {
            return true;
        }

        var access = await media.GetMediaUserAsync(item.Id, caller.UserId, cancellationToken);
        if (access is { IsHidden: true } or { Role: Permission.Viewer })
        {
            return false;
        }

        if (access is { Role: Permission.Owner or Permission.Editor })
        {
            return true;
        }

        return caller.CanWriteStudioContent(item.OwnerId);
    }

    private async Task<bool> CanAccessAsync(Models.Media item, CallerContext caller, CancellationToken cancellationToken)
    {
        if (item.OwnerKind == OwnerKind.Studio)
        {
            if (caller.IsStudioOwner(item.OwnerId))
            {
                return true;
            }

            if (!caller.InStudio(item.OwnerId))
            {
                return false;
            }

            var studioOverride = await media.GetMediaUserAsync(item.Id, caller.UserId, cancellationToken);
            return studioOverride?.IsHidden != true;
        }

        if (CanRead(item, caller))
        {
            return true;
        }

        var directShare = await media.GetMediaUserAsync(item.Id, caller.UserId, cancellationToken);
        if (directShare is { IsHidden: true })
        {
            return false;
        }

        return directShare is { IsHidden: false }
            || await albums.UserHasVisibleAlbumAccessToMediaAsync(item.Id, caller.UserId, cancellationToken);
    }

    private static OwnerKind OwnerKindOf(WorkspaceScope scope) => scope.IsStudio ? OwnerKind.Studio : OwnerKind.User;

    private static (int Page, int PageSize) Normalize(int page, int pageSize) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, 100));

    private static MediaUser CreateMediaUser(Models.Media item, Guid userId, Permission role) =>
        item switch
        {
            Video => new VideoUser { MediaId = item.Id, UserId = userId, Role = role, IsFavorite = false, IsHidden = false },
            Audio => new AudioUser { MediaId = item.Id, UserId = userId, Role = role, IsFavorite = false, IsHidden = false },
            Photo => new PhotoUser { MediaId = item.Id, UserId = userId, Role = role, IsFavorite = false, IsHidden = false },
            _ => throw new NotSupportedException()
        };

    private async Task<IReadOnlyList<Models.Media>> FilterVisibleAsync(
        IReadOnlyList<Models.Media> items,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        var visible = new List<Models.Media>();
        foreach (var item in items)
        {
            if (await CanAccessAsync(item, caller, cancellationToken))
            {
                visible.Add(item);
            }
        }

        return visible;
    }

    private async Task<IReadOnlyList<MediaAccessMemberDto>> BuildAccessRowsAsync(
        Models.Media item,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        var members = await auth.ListStudioMembersAsync(item.OwnerId, cancellationToken);
        var rows = new List<MediaAccessMemberDto>();
        foreach (var member in members)
        {
            var access = await media.GetMediaUserAsync(item.Id, member.UserId, cancellationToken);
            rows.Add(new MediaAccessMemberDto(
                member.UserId,
                member.Email,
                member.DisplayName,
                member.Role,
                access?.Role ?? DefaultPermissionForStudioRole(member.Role),
                access?.Role,
                access?.IsHidden ?? false,
                CanManageTarget(caller, item.OwnerId, member.Role)));
        }

        return rows;
    }

    private static void RequireStudioAccessManage(Models.Media item, CallerContext caller)
    {
        if (item.OwnerKind != OwnerKind.Studio)
        {
            throw DomainException.BadRequest("Item access overrides are only available for Studio media.");
        }

        if (!caller.CanManageStudioAccess(item.OwnerId))
        {
            throw DomainException.Forbidden("You do not have permission to manage item access.");
        }
    }

    private static void RequireCanManageTarget(CallerContext caller, Guid studioId, string targetRole)
    {
        if (!CanManageTarget(caller, studioId, targetRole))
        {
            throw DomainException.Forbidden("You cannot restrict a member with that Studio role.");
        }
    }

    private static bool CanManageTarget(CallerContext caller, Guid studioId, string targetRole)
    {
        if (caller.IsStudioOwner(studioId))
        {
            return !string.Equals(targetRole, "Owner", StringComparison.Ordinal);
        }

        return caller.IsStudioAdmin(studioId)
            && !string.Equals(targetRole, "Owner", StringComparison.Ordinal)
            && !string.Equals(targetRole, "Admin", StringComparison.Ordinal);
    }

    private static Permission DefaultPermissionForStudioRole(string studioRole) =>
        string.Equals(studioRole, "Viewer", StringComparison.Ordinal)
            ? Permission.Viewer
            : Permission.Editor;

    private async Task<IReadOnlyDictionary<Guid, UserSummary>> GetUserOwnerSummariesAsync(
        IEnumerable<Models.Media> mediaItems,
        CancellationToken cancellationToken)
    {
        var owners = new Dictionary<Guid, UserSummary>();
        foreach (var ownerId in mediaItems
            .Where(item => item.OwnerKind == OwnerKind.User)
            .Select(item => item.OwnerId)
            .Distinct())
        {
            var summary = await auth.GetSummaryAsync(ownerId, cancellationToken);
            if (summary is not null)
            {
                owners[ownerId] = summary;
            }
        }

        return owners;
    }

    internal static MediaDto ToDto(Models.Media m, bool isFavorite = false, UserSummary? owner = null)
    {
        double? duration = m switch { Video v => v.DurationSeconds, Audio a => a.DurationSeconds, _ => null };
        int? width = m switch { Video v => v.Width, Photo p => p.Width, _ => null };
        int? height = m switch { Video v => v.Height, Photo p => p.Height, _ => null };
        double? frameRate = m switch { Video v => v.FrameRate, _ => null };
        
        return new(
            m.Id,
            m.OwnerId,
            m.OwnerKind,
            owner?.Email,
            owner?.DisplayName,
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
            m.CreatedAt,
            isFavorite,
            ToPipelineDto(m));
    }

    private static MediaPipelineDto ToPipelineDto(Models.Media m)
    {
        return m.Status switch
        {
            MediaStatus.Queued => new MediaPipelineDto(
                "queued",
                "Queued",
                "Waiting to start processing.",
                1,
                4,
                false),
            MediaStatus.Uploaded => new MediaPipelineDto(
                "optimizing",
                "Optimizing media",
                "Upload saved. Kuvox is generating optimized media and previews.",
                2,
                4,
                false),
            MediaStatus.Processing => new MediaPipelineDto(
                m.Kind == MediaKind.Video ? "ingesting" : "optimizing",
                m.Kind == MediaKind.Video ? "Analyzing video" : "Optimizing media",
                m.Kind == MediaKind.Video
                    ? "Optimized media is ready. Kuvox is indexing shots and AI context."
                    : "Kuvox is finalizing optimized media.",
                3,
                4,
                false),
            MediaStatus.Ready => new MediaPipelineDto(
                "ready",
                "Ready to edit",
                "Import and processing completed.",
                4,
                4,
                true),
            MediaStatus.Failed => new MediaPipelineDto(
                "failed",
                "Import failed",
                string.IsNullOrWhiteSpace(m.ErrorMessage)
                    ? "Kuvox could not finish importing this file."
                    : m.ErrorMessage,
                4,
                4,
                true),
            _ => new MediaPipelineDto(
                m.Status.ToString().ToLowerInvariant(),
                m.Status.ToString(),
                "Import status is being updated.",
                1,
                4,
                false)
        };
    }

    private static MediaTrashItemDto ToTrashDto(Models.Media m)
    {
        var deletedAt = m.DeletedAt ?? DateTimeOffset.UtcNow;
        var remaining = (deletedAt + TrashRetention) - DateTimeOffset.UtcNow;
        var purgesInDays = Math.Max(0, (int)Math.Ceiling(remaining.TotalDays));
        return new MediaTrashItemDto(m.Id, m.Kind, m.Filename, deletedAt, purgesInDays);
    }
}
