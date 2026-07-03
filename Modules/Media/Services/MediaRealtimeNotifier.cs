using Kuvox.Api.Modules.Media.Dtos;
using Kuvox.Api.Modules.Media.Enums;
using Microsoft.AspNetCore.SignalR;
using MediaEntity = Kuvox.Api.Modules.Media.Models.Media;

namespace Kuvox.Api.Modules.Media.Services;

internal sealed class MediaRealtimeNotifier(
    IHubContext<MediaHub> hub,
    ILogger<MediaRealtimeNotifier> logger)
    : IMediaRealtimeNotifier
{
    public static string UserGroup(Guid userId) => $"user:{userId}";

    public static string StudioGroup(Guid studioId) => $"studio:{studioId}";

    public async Task MediaUpdatedAsync(
        MediaEntity media,
        MediaDto dto,
        string phase,
        CancellationToken cancellationToken = default,
        int? shotCount = null,
        string? errorCode = null,
        string? errorMessage = null)
    {
        var group = media.OwnerKind == OwnerKind.Studio
            ? StudioGroup(media.OwnerId)
            : UserGroup(media.OwnerId);

        try
        {
            await hub.Clients
                .Group(group)
                .SendAsync(
                    "mediaUpdated",
                    new MediaRealtimeUpdate(
                        dto,
                        phase,
                        DateTimeOffset.UtcNow,
                        dto.Pipeline,
                        shotCount,
                        errorCode,
                        errorMessage),
                    cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "[Media] Failed to publish realtime update for media {MediaId}.",
                media.Id);
        }
    }
}
