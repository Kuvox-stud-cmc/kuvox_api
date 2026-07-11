using Kuvox.Api.Modules.Timelines.Models;

namespace Kuvox.Api.Modules.Timelines.Services;

internal interface IRenderRealtimeNotifier
{
    Task RenderJobUpdatedAsync(RenderJob job, CancellationToken cancellationToken = default);
}
