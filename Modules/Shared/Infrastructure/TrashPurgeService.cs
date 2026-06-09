using Kuvox.Api.Modules.Media.Contracts;
using Kuvox.Api.Modules.Media.Repositories;
using Kuvox.Api.Modules.Projects.Contracts;
using Kuvox.Api.Modules.Projects.Repositories;
using MediatR;

namespace Kuvox.Api.Modules.Shared.Infrastructure;

/// <summary>
/// Background job that empties Trash: every <see cref="Interval"/> it hard-deletes any
/// <c>Project</c>/<c>Media</c> soft-deleted more than <see cref="Retention"/> ago and publishes
/// the matching delete events so dependent modules clean up (Rule 4).
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

        var staleProjects = await projects.ListDeletedBeforeAsync(cutoff, cancellationToken);
        if (staleProjects.Count > 0)
        {
            foreach (var project in staleProjects)
            {
                projects.Remove(project);
            }

            await projects.SaveChangesAsync(cancellationToken);

            foreach (var project in staleProjects)
            {
                await mediator.Publish(new ProjectDeletedEvent(project.Id), cancellationToken);
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
                await mediator.Publish(new MediaDeletedEvent(item.Id), cancellationToken);
            }

            logger.LogInformation(
                "[TrashPurge] purged {Count} media item(s) older than {Days}d.",
                staleMedia.Count, Retention.TotalDays);
        }
    }
}
