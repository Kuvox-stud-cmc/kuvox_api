using Kuvox.Api.Modules.Auth.Contracts;
using Kuvox.Api.Modules.Media.Enums;
using Kuvox.Api.Modules.Media.Repositories;
using Kuvox.Api.Modules.Media.Contracts;
using Kuvox.Api.Modules.Shared.Infrastructure.Caching;
using MediatR;
using Microsoft.Extensions.Options;

namespace Kuvox.Api.Modules.Media.Services;

internal sealed class StudioDeletedHandler(
    IMediaRepository media,
    IAlbumRepository albums,
    BusinessCache cache,
    CacheGenerationManager generations,
    IOptions<CachingOptions> options,
    IMediator mediator) : INotificationHandler<StudioDeletedEvent>
{
    public async Task Handle(StudioDeletedEvent notification, CancellationToken cancellationToken)
    {
        var (items, _) = await media.ListByWorkspaceAsync(OwnerKind.Studio, notification.StudioId, 1, int.MaxValue, cancellationToken);
        var studioAlbums = await albums.ListByWorkspaceAsync(
            OwnerKind.Studio, notification.StudioId, Guid.Empty, includeSystem: true, cancellationToken);
        foreach (var item in items)
        {
            media.Remove(item);
        }
        foreach (var album in studioAlbums)
        {
            albums.Remove(album);
        }
        await media.SaveChangesAsync(cancellationToken);
        if (cache.IsEnabled(options.Value.Media))
        {
            _ = await generations.BumpAsync("media", $"owner-Studio-{notification.StudioId:N}", CancellationToken.None);
            _ = await generations.BumpAsync("media", "shared-global", CancellationToken.None);
        }
        if (cache.IsEnabled(options.Value.StorageUsage))
        {
            _ = await generations.BumpAsync("storage-usage", $"owner-Studio-{notification.StudioId:N}", CancellationToken.None);
        }
        if (cache.IsEnabled(options.Value.Albums))
        {
            _ = await generations.BumpAsync("albums", $"owner-Studio-{notification.StudioId:N}", CancellationToken.None);
            _ = await generations.BumpAsync("albums", "shared-global", CancellationToken.None);
        }
        foreach (var item in items)
        {
            await mediator.Publish(new MediaProjectionChangedEvent(item.Id), cancellationToken);
        }
    }
}
