using Kuvox.Api.Modules.Notifications.Dtos;
using Kuvox.Api.Modules.Notifications.Enums;
using Kuvox.Api.Modules.Notifications.Models;
using Kuvox.Api.Modules.Notifications.Repositories;
using Kuvox.Api.Modules.Shared.Dtos;
using Kuvox.Api.Modules.Shared.Infrastructure;
using Kuvox.Api.Modules.Shared.Infrastructure.Caching;
using Microsoft.Extensions.Options;

namespace Kuvox.Api.Modules.Notifications.Services;

internal sealed class NotificationsService(
    INotificationsRepository notifications,
    BusinessCache cache,
    CacheKeyFactory cacheKeys,
    NotificationCacheInvalidator invalidator,
    IOptions<CachingOptions> cachingOptions) : INotificationsService
{
    private readonly CachingOptions _caching = cachingOptions.Value;

    public async Task<PagedResult<NotificationDto>> ListMineAsync(
        Guid userId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Normalize(page, pageSize);
        var generation = await invalidator.GetPageGenerationAsync(userId, cancellationToken);
        async Task<PagedResult<NotificationDto>> Load(CancellationToken ct)
        {
            var result = await notifications.ListForUserAsync(userId, page, pageSize, ct);
            return new PagedResult<NotificationDto>(result.Items.Select(ToDto).ToList(), result.Page, result.PageSize, result.TotalCount);
        }

        if (generation is null)
        {
            return await Load(cancellationToken);
        }

        var key = BusinessCacheKey.Create(
            cacheKeys, "notification-page", "user", userId, "page", page, "size", pageSize, "gen", generation);
        return await cache.GetOrCreateAsync(
            "notifications", "list", _caching.Notifications, key,
            TimeSpan.FromSeconds(_caching.Notifications.TtlSeconds), Load, cancellationToken);
    }

    public Task<UnreadCountDto> CountUnreadAsync(Guid userId, CancellationToken cancellationToken = default) =>
        cache.GetOrCreateAsync(
            "notification-count", "count", _caching.NotificationCount, invalidator.CountKey(userId),
            TimeSpan.FromSeconds(_caching.NotificationCount.TtlSeconds),
            async ct => new UnreadCountDto(await notifications.CountUnreadAsync(userId, ct)),
            cancellationToken);

    public async Task<NotificationDto> MarkReadAsync(
        Guid userId, Guid notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await LoadAsync(userId, notificationId, cancellationToken);
        MarkRead(notification);
        await notifications.SaveChangesAsync(cancellationToken);
        await invalidator.InvalidateAsync(userId);
        return ToDto(notification);
    }

    public async Task MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await notifications.MarkAllReadAsync(userId, cancellationToken);
        await notifications.SaveChangesAsync(cancellationToken);
        await invalidator.InvalidateAsync(userId);
    }

    public async Task<NotificationDto> ArchiveAsync(
        Guid userId, Guid notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await LoadAsync(userId, notificationId, cancellationToken);
        notification.Status = NotificationStatus.Archived;
        await notifications.SaveChangesAsync(cancellationToken);
        await invalidator.InvalidateAsync(userId);
        return ToDto(notification);
    }

    public async Task DeleteAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await LoadAsync(userId, notificationId, cancellationToken);
        notification.Status = NotificationStatus.Deleted;
        await notifications.SaveChangesAsync(cancellationToken);
        await invalidator.InvalidateAsync(userId);
    }

    private async Task<Notification> LoadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken)
    {
        var notification = await notifications.GetForUserAsync(notificationId, userId, cancellationToken)
            ?? throw DomainException.NotFound("Notification not found.");

        if (notification.Status == NotificationStatus.Deleted)
        {
            throw DomainException.NotFound("Notification not found.");
        }

        return notification;
    }

    private static void MarkRead(Notification notification)
    {
        if (notification.Status != NotificationStatus.Read)
        {
            notification.Status = NotificationStatus.Read;
            notification.ReadAt = DateTimeOffset.UtcNow;
        }
    }

    internal static NotificationDto ToDto(Notification n) =>
        new(n.Id, n.UserId, n.StudioId, n.Type, n.Status, n.Message, n.LinkUrl, n.CreatedAt, n.ReadAt);

    private static (int Page, int PageSize) Normalize(int page, int pageSize) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, 100));
}
