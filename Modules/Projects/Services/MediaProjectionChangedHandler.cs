using Kuvox.Api.Modules.Media.Contracts;
using Kuvox.Api.Modules.Shared.Infrastructure.Caching;
using MediatR;
using Microsoft.Extensions.Options;

namespace Kuvox.Api.Modules.Projects.Services;

internal sealed class MediaProjectionChangedHandler(
    BusinessCache cache,
    CacheGenerationManager generations,
    IOptions<CachingOptions> options) : INotificationHandler<MediaProjectionChangedEvent>
{
    private readonly ProjectCacheOptions _options = options.Value.Projects;

    public async Task Handle(MediaProjectionChangedEvent notification, CancellationToken cancellationToken)
    {
        if (cache.IsEnabled(_options))
        {
            _ = await generations.BumpAsync("media-projection", "global", CancellationToken.None);
        }
    }
}
