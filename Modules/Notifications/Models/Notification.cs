using Kuvox.Api.Modules.Shared.Models;
using Kuvox.Api.Modules.Notifications.Enums;
namespace Kuvox.Api.Modules.Notifications.Models;

public sealed class Notification : ImmutableBaseEntity
{
    public required Guid UserId { get; set; }

    public required Guid StudioId { get; set; }

    public required NotificationType Type { get; set; }

    public required NotificationStatus Status { get; set; }

    public required string Message { get; set; }
}