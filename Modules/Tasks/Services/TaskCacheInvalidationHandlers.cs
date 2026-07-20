using Kuvox.Api.Modules.Auth.Contracts;
using Kuvox.Api.Modules.Projects.Contracts;
using Kuvox.Api.Modules.Shared.Infrastructure.Caching;
using MediatR;
using Microsoft.Extensions.Options;

namespace Kuvox.Api.Modules.Tasks.Services;

internal sealed class TaskStudioMembershipChangedHandler(
    BusinessCache cache,
    CacheGenerationManager generations,
    IOptions<CachingOptions> options) : INotificationHandler<StudioMembershipChangedEvent>
{
    private readonly TaskCacheOptions _options = options.Value.Tasks;

    public async Task Handle(StudioMembershipChangedEvent notification, CancellationToken cancellationToken)
    {
        if (cache.IsEnabled(_options))
        {
            _ = await generations.BumpAsync("tasks", $"studio-{notification.StudioId:N}", CancellationToken.None);
        }
    }
}

internal sealed class ProjectSummaryChangedHandler(
    BusinessCache cache,
    CacheGenerationManager generations,
    IOptions<CachingOptions> options) : INotificationHandler<ProjectSummaryChangedEvent>
{
    private readonly TaskCacheOptions _options = options.Value.Tasks;

    public async Task Handle(ProjectSummaryChangedEvent notification, CancellationToken cancellationToken)
    {
        if (cache.IsEnabled(_options))
        {
            _ = await generations.BumpAsync("tasks", $"studio-{notification.StudioId:N}", CancellationToken.None);
        }
    }
}

internal sealed class TaskStudioDeletedHandler(
    BusinessCache cache,
    CacheGenerationManager generations,
    IOptions<CachingOptions> options) : INotificationHandler<StudioDeletedEvent>
{
    private readonly TaskCacheOptions _options = options.Value.Tasks;

    public async Task Handle(StudioDeletedEvent notification, CancellationToken cancellationToken)
    {
        if (cache.IsEnabled(_options))
        {
            _ = await generations.BumpAsync("tasks", $"studio-{notification.StudioId:N}", CancellationToken.None);
        }
    }
}
