using Kuvox.Api.Modules.Shared.Infrastructure.Caching;
using Microsoft.Extensions.Options;

namespace Kuvox.Api.Modules.Notifications.Services;

internal sealed class NotificationCacheInvalidator(
    BusinessCache cache,
    CacheGenerationManager generations,
    CacheKeyFactory keys,
    IOptions<CachingOptions> options)
{
    private readonly CachingOptions _options = options.Value;
    public string CountKey(Guid userId) =>
        BusinessCacheKey.Create(keys, "notification-count", "user", userId);

    public Task<string?> GetPageGenerationAsync(Guid userId, CancellationToken cancellationToken = default) =>
        cache.IsEnabled(_options.Notifications)
            ? generations.GetAsync("notifications", $"user-{userId:N}", cancellationToken)
            : Task.FromResult<string?>(null);

    public async Task InvalidateAsync(Guid userId)
    {
        await cache.InvalidateExactAsync("notification-count", CountKey(userId), _options.NotificationCount);
        if (cache.IsEnabled(_options.Notifications))
        {
            await generations.BumpAsync("notifications", $"user-{userId:N}");
        }
    }
}
