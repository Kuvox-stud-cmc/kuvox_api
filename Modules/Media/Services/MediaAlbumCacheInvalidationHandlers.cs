using Kuvox.Api.Modules.Auth.Contracts;
using Kuvox.Api.Modules.Media.Contracts;
using Kuvox.Api.Modules.Shared.Infrastructure.Caching;
using MediatR;
using Microsoft.Extensions.Options;

namespace Kuvox.Api.Modules.Media.Services;

internal sealed class MediaStudioMembershipChangedHandler(
    BusinessCache cache,
    CacheGenerationManager generations,
    IOptions<CachingOptions> options) : INotificationHandler<StudioMembershipChangedEvent>
{
    private readonly CachingOptions _options = options.Value;

    public async Task Handle(StudioMembershipChangedEvent notification, CancellationToken cancellationToken)
    {
        if (cache.IsEnabled(_options.Media))
        {
            _ = await generations.BumpAsync("media", $"owner-Studio-{notification.StudioId:N}", CancellationToken.None);
            _ = await generations.BumpAsync("media", "shared-global", CancellationToken.None);
        }
        if (cache.IsEnabled(_options.Albums))
        {
            _ = await generations.BumpAsync("albums", $"owner-Studio-{notification.StudioId:N}", CancellationToken.None);
            _ = await generations.BumpAsync("albums", "shared-global", CancellationToken.None);
        }
    }
}

internal sealed class AlbumMediaProjectionChangedHandler(
    BusinessCache cache,
    CacheGenerationManager generations,
    IOptions<CachingOptions> options) : INotificationHandler<MediaProjectionChangedEvent>
{
    private readonly CacheFeatureOptions _options = options.Value.Albums;

    public async Task Handle(MediaProjectionChangedEvent notification, CancellationToken cancellationToken)
    {
        if (cache.IsEnabled(_options))
        {
            _ = await generations.BumpAsync("media-projection", "global", CancellationToken.None);
        }
    }
}
