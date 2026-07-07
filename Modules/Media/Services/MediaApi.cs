using Kuvox.Api.Modules.Media.Contracts;
using Kuvox.Api.Modules.Media.Enums;
using Kuvox.Api.Modules.Media.Models;
using Kuvox.Api.Modules.Media.Repositories;
using Kuvox.Api.Modules.Shared.Infrastructure;

namespace Kuvox.Api.Modules.Media.Services;

/// <summary>Implements the public <see cref="IMediaApi"/> read facade (Rule 2). Internal (Rule 1).</summary>
internal sealed class MediaApi(IMediaRepository media, IAlbumRepository albums) : IMediaApi
{
    public async Task<MediaSummary?> GetSummaryAsync(Guid mediaId, CancellationToken cancellationToken = default)
    {
        var item = await media.GetByIdAsync(mediaId, cancellationToken);
        return item is null
            ? null
            : ToSummary(item);
    }

    public async Task<IReadOnlyList<MediaResolution>> ResolveAsync(
        IReadOnlyCollection<Guid> mediaIds,
        CallerContext caller,
        CancellationToken cancellationToken = default)
    {
        var ids = mediaIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        var items = (await media.ListByIdsAsync(ids, cancellationToken)).ToDictionary(item => item.Id);
        var resolved = new List<MediaResolution>(ids.Length);
        foreach (var mediaId in ids)
        {
            if (!items.TryGetValue(mediaId, out var item))
            {
                resolved.Add(new MediaResolution(mediaId, null, MediaResolutionAvailability.Missing, null));
                continue;
            }

            if (item.DeletedAt is not null)
            {
                resolved.Add(new MediaResolution(
                    mediaId,
                    item.Kind,
                    MediaResolutionAvailability.Deleted,
                    SafePlaceholder(item)));
                continue;
            }

            if (!await CanAccessAsync(item, caller, cancellationToken))
            {
                resolved.Add(new MediaResolution(
                    mediaId,
                    item.Kind,
                    MediaResolutionAvailability.Inaccessible,
                    SafePlaceholder(item)));
                continue;
            }

            var availability = item.Status switch
            {
                MediaStatus.Ready => MediaResolutionAvailability.Available,
                MediaStatus.Failed => MediaResolutionAvailability.Failed,
                _ => MediaResolutionAvailability.Processing,
            };
            resolved.Add(new MediaResolution(mediaId, item.Kind, availability, ToSummary(item)));
        }

        return resolved;
    }

    public async Task<MediaWorkspaceUsageSummary> GetWorkspaceUsageAsync(
        Guid ownerId,
        Enums.OwnerKind ownerKind,
        CancellationToken cancellationToken = default)
    {
        var usage = await media.GetStorageUsageAsync(ownerKind, ownerId, cancellationToken);
        return new MediaWorkspaceUsageSummary(usage.MediaCount, usage.StorageBytesUsed);
    }

    private async Task<bool> CanAccessAsync(Models.Media item, CallerContext caller, CancellationToken cancellationToken)
    {
        if (item.OwnerKind == Enums.OwnerKind.Studio)
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

        if (caller.OwnsAsUser(item.OwnerId))
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

    private static MediaSummary SafePlaceholder(Models.Media item) =>
        new(
            item.Id,
            item.OwnerId,
            item.OwnerKind,
            item.Kind,
            item.Filename,
            item.Status.ToString(),
            IsDeleted: item.DeletedAt is not null);

    private static MediaSummary ToSummary(Models.Media item)
    {
        double? duration = item switch { Video video => video.DurationSeconds, Audio audio => audio.DurationSeconds, _ => null };
        int? width = item switch { Video video => video.Width, Photo photo => photo.Width, _ => null };
        int? height = item switch { Video video => video.Height, Photo photo => photo.Height, _ => null };
        double? frameRate = item switch { Video video => video.FrameRate, _ => null };

        return new MediaSummary(
            item.Id,
            item.OwnerId,
            item.OwnerKind,
            item.Kind,
            item.Filename,
            item.Status.ToString(),
            IsDeleted: item.DeletedAt is not null,
            StorageKey: item.StorageKey,
            SizeBytes: item.SizeBytes,
            CanonicalStorageKey: item.CanonicalStorageKey,
            ProxyStorageKey: item.ProxyStorageKey,
            ThumbnailStorageKey: item.ThumbnailStorageKey,
            ErrorMessage: item.ErrorMessage,
            DurationSeconds: duration,
            Width: width,
            Height: height,
            Codec: item.Codec,
            FrameRate: frameRate,
            CreatedAt: item.CreatedAt);
    }
}
