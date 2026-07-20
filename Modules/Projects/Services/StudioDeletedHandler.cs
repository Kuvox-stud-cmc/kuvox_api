using Kuvox.Api.Modules.Auth.Contracts;
using Kuvox.Api.Modules.Projects.Enums;
using Kuvox.Api.Modules.Projects.Repositories;
using Kuvox.Api.Modules.Shared.Infrastructure.Caching;
using MediatR;
using Microsoft.Extensions.Options;

namespace Kuvox.Api.Modules.Projects.Services;

internal sealed class StudioDeletedHandler(
    IProjectRepository projects,
    BusinessCache cache,
    CacheGenerationManager generations,
    IOptions<CachingOptions> options) : INotificationHandler<StudioDeletedEvent>
{
    public async Task Handle(StudioDeletedEvent notification, CancellationToken cancellationToken)
    {
        var (items, _) = await projects.ListByWorkspaceAsync(OwnerKind.Studio, notification.StudioId, 1, int.MaxValue, cancellationToken);
        foreach (var item in items)
        {
            item.DeletedAt = DateTimeOffset.UtcNow;
            item.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await projects.SaveChangesAsync(cancellationToken);
        if (cache.IsEnabled(options.Value.Projects))
        {
            _ = await generations.BumpAsync("projects", $"owner-Studio-{notification.StudioId:N}", CancellationToken.None);
            _ = await generations.BumpAsync("projects", "shared-global", CancellationToken.None);
            foreach (var item in items)
            {
                _ = await generations.BumpAsync("projects", $"project-{item.Id:N}", CancellationToken.None);
            }
        }
    }
}
