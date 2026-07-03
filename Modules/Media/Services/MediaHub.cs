using Kuvox.Api.Modules.Shared.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Kuvox.Api.Modules.Media.Services;

[Authorize]
public sealed class MediaHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (Context.User?.GetUserId() is { } userId)
        {
            await Groups.AddToGroupAsync(
                Context.ConnectionId,
                MediaRealtimeNotifier.UserGroup(userId));
        }

        foreach (var (studioId, _) in Context.User?.GetStudios() ?? [])
        {
            await Groups.AddToGroupAsync(
                Context.ConnectionId,
                MediaRealtimeNotifier.StudioGroup(studioId));
        }

        await base.OnConnectedAsync();
    }
}
