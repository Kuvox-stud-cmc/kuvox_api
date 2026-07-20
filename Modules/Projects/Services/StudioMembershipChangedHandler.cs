using Kuvox.Api.Modules.Auth.Contracts;
using Kuvox.Api.Modules.Shared.Infrastructure.Caching;
using MediatR;
using Microsoft.Extensions.Options;

namespace Kuvox.Api.Modules.Projects.Services;

internal sealed class StudioMembershipChangedHandler(
    BusinessCache cache,
    CacheGenerationManager generations,
    IOptions<CachingOptions> options) : INotificationHandler<StudioMembershipChangedEvent>
{
    private readonly ProjectCacheOptions _options = options.Value.Projects;

    public async Task Handle(StudioMembershipChangedEvent notification, CancellationToken cancellationToken)
    {
        if (!cache.IsEnabled(_options)) return;
        _ = await generations.BumpAsync("projects", $"owner-Studio-{notification.StudioId:N}", CancellationToken.None);
        _ = await generations.BumpAsync("projects", "shared-global", CancellationToken.None);
    }
}
