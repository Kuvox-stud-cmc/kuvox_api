using Kuvox.Api.Modules.Media.Services;
using Kuvox.Api.Modules.Timelines.Models;
using Microsoft.AspNetCore.SignalR;

namespace Kuvox.Api.Modules.Timelines.Services;

internal sealed class RenderRealtimeNotifier(
    IHubContext<MediaHub> hub,
    ILogger<RenderRealtimeNotifier> logger)
    : IRenderRealtimeNotifier
{
    public async Task RenderJobUpdatedAsync(
        RenderJob job,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await hub.Clients
                .Group(MediaRealtimeNotifier.UserGroup(job.RequestedByUserId))
                .SendAsync(
                    "renderJobUpdated",
                    RenderRealtimeUpdate.FromJob(job),
                    cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "[Timelines] Failed to publish realtime update for render job {RenderJobId}.",
                job.Id);
        }
    }
}
