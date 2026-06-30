using Kuvox.Api.Modules.Notifications.Enums;
using Kuvox.Api.Modules.Notifications.Models;
using Kuvox.Api.Modules.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Kuvox.Api.Modules.Notifications.Repositories;

internal sealed class NotificationsRepository(NotificationsDbContext db) : INotificationsRepository
{
    public async Task AddAsync(Notification notification, CancellationToken cancellationToken = default) =>
        await db.Notifications.AddAsync(notification, cancellationToken);

    public Task<Notification?> GetForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) =>
        db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken);

    public async Task<PagedResult<Notification>> ListForUserAsync(
        Guid userId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = db.Notifications
            .Where(n => n.UserId == userId && n.Status != NotificationStatus.Deleted)
            .OrderByDescending(n => n.CreatedAt);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Notification>(items, page, pageSize, total);
    }

    public Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.Notifications.CountAsync(n => n.UserId == userId && n.Status == NotificationStatus.Unread, cancellationToken);

    public async Task MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var unread = await db.Notifications
            .Where(n => n.UserId == userId && n.Status == NotificationStatus.Unread)
            .ToListAsync(cancellationToken);

        foreach (var notification in unread)
        {
            notification.Status = NotificationStatus.Read;
            notification.ReadAt = now;
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
