using Kuvox.Api.Modules.Media.Contracts;
using Kuvox.Api.Modules.Media.Repositories;
using Kuvox.Api.Modules.Projects.Repositories;
using Kuvox.Api.Modules.Projects.Contracts;
using Kuvox.Api.Modules.Shared.Infrastructure.Caching;
using Microsoft.Extensions.Options;
using MediatR;

namespace Kuvox.Api.Modules.Shared.Infrastructure;

/// <summary>
/// Background job that empties Trash: every <see cref="Interval"/> it hard-deletes any
/// <c>Project</c>/<c>Media</c> soft-deleted more than <see cref="Retention"/> ago and publishes
/// media delete events so dependent modules clean up media associations (Rule 4).
///
/// Lives in Shared because it spans modules; it resolves each module's (internal) repository in
/// a fresh DI scope per run — the modular-monolith equivalent of one maintenance worker calling
/// into each module's persistence boundary.
/// </summary>
public sealed class TrashPurgeService(IServiceProvider services, ILogger<TrashPurgeService> logger)
    : BackgroundService
{
    /// <summary>How long a soft-deleted item survives in Trash before auto-purge (ABOUT.md: 7 days).</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[TrashPurge] purge run failed; retrying next interval.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PurgeOnceAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow - Retention;

        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var projects = sp.GetRequiredService<IProjectRepository>();
        var media = sp.GetRequiredService<IMediaRepository>();
        var mediator = sp.GetRequiredService<IMediator>();
        var cache = sp.GetRequiredService<BusinessCache>();
        var generations = sp.GetRequiredService<CacheGenerationManager>();
        var caching = sp.GetRequiredService<IOptions<CachingOptions>>().Value;

        var staleProjects = await projects.ListDeletedBeforeAsync(cutoff, cancellationToken);
        if (staleProjects.Count > 0)
        {
            foreach (var project in staleProjects)
            {
                projects.Remove(project);
            }

            await projects.SaveChangesAsync(cancellationToken);
            if (cache.IsEnabled(caching.Projects))
            {
                foreach (var project in staleProjects)
                {
                    _ = await generations.BumpAsync("projects", $"project-{project.Id:N}");
                    _ = await generations.BumpAsync("projects", $"owner-{project.OwnerKind}-{project.OwnerId:N}");
                    if (project.OwnerKind == Modules.Projects.Enums.OwnerKind.Studio)
                    {
                        await mediator.Publish(new ProjectSummaryChangedEvent(project.OwnerId), cancellationToken);
                    }
                }
                _ = await generations.BumpAsync("projects", "shared-global");
            }

            logger.LogInformation(
                "[TrashPurge] purged {Count} project(s) older than {Days}d.",
                staleProjects.Count, Retention.TotalDays);
        }

        var staleMedia = await media.ListDeletedBeforeAsync(cutoff, cancellationToken);
        if (staleMedia.Count > 0)
        {
            foreach (var item in staleMedia)
            {
                media.Remove(item);
            }

            await media.SaveChangesAsync(cancellationToken);

            foreach (var item in staleMedia)
            {
                if (cache.IsEnabled(caching.Media))
                {
                    _ = await generations.BumpAsync("media", $"media-{item.Id:N}");
                    _ = await generations.BumpAsync("media", $"owner-{item.OwnerKind}-{item.OwnerId:N}");
                }
                if (cache.IsEnabled(caching.StorageUsage))
                {
                    _ = await generations.BumpAsync("storage-usage", $"owner-{item.OwnerKind}-{item.OwnerId:N}");
                }
                await mediator.Publish(new MediaProjectionChangedEvent(item.Id), cancellationToken);
                await mediator.Publish(new MediaDeletedEvent(item.Id), cancellationToken);
            }
            if (cache.IsEnabled(caching.Media))
            {
                _ = await generations.BumpAsync("media", "shared-global");
            }

            logger.LogInformation(
                "[TrashPurge] purged {Count} media item(s) older than {Days}d.",
                staleMedia.Count, Retention.TotalDays);
        }
    }
}
