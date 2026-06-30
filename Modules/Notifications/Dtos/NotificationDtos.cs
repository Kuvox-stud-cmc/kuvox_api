using Kuvox.Api.Modules.Notifications.Enums;

namespace Kuvox.Api.Modules.Notifications.Dtos;

public sealed record NotificationDto(
    Guid Id,
    Guid UserId,
    Guid? StudioId,
    NotificationType Type,
    NotificationStatus Status,
    string Message,
    string? LinkUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

public sealed record UnreadCountDto(int Count);
