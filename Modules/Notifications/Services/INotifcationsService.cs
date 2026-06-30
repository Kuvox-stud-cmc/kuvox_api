using Kuvox.Api.Modules.Notifications.Dtos;
using Kuvox.Api.Modules.Shared.Dtos;

namespace Kuvox.Api.Modules.Notifications.Services;

public interface INotificationsService
{
    Task<PagedResult<NotificationDto>> ListMineAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<UnreadCountDto> CountUnreadAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<NotificationDto> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default);

    Task MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<NotificationDto> ArchiveAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default);
}
