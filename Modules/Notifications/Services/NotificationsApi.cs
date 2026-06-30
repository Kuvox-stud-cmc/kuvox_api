using Kuvox.Api.Modules.Notifications.Enums;
using Kuvox.Api.Modules.Notifications.Models;
using Kuvox.Api.Modules.Notifications.Repositories;

namespace Kuvox.Api.Modules.Notifications;

internal sealed class NotificationsApi(INotificationsRepository notifications) : INotificationsApi
{
    public async Task CreateAsync(
        Guid userId,
        Guid? studioId,
        string type,
        string message,
        string? linkUrl = null,
        CancellationToken cancellationToken = default)
    {
        var parsedType = Enum.TryParse<NotificationType>(type, ignoreCase: true, out var notificationType)
            ? notificationType
            : NotificationType.StudioSettingsChanged;

        await notifications.AddAsync(new Notification
        {
            UserId = userId,
            StudioId = studioId,
            Type = parsedType,
            Status = NotificationStatus.Unread,
            Message = message,
            LinkUrl = linkUrl,
        }, cancellationToken);
        await notifications.SaveChangesAsync(cancellationToken);
    }
}
