using Kuvox.Api.Modules.Notifications.Enums;
using Kuvox.Api.Modules.Notifications.Models;
using Kuvox.Api.Modules.Shared.Dtos;

namespace Kuvox.Api.Modules.Notifications.Repositories;

internal interface INotificationsRepository
{
    Task AddAsync(Notification notification, CancellationToken cancellationToken = default);

    Task<Notification?> GetForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);

    Task<PagedResult<Notification>> ListForUserAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken = default);

    Task MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
