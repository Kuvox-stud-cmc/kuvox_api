using Microsoft.Extensions.Options;

namespace Kuvox.Api.Modules.Shared.Infrastructure.Caching;

public static class CachingInfrastructure
{
    public static IServiceCollection AddCachingInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<CachingOptions>()
            .Bind(configuration.GetSection(CachingOptions.SectionName))
            .Validate(options => options.ConnectTimeoutMilliseconds > 0)
            .Validate(options => options.OperationTimeoutMilliseconds > 0)
            .Validate(options => options.MaxPayloadBytes > 0)
            .Validate(options => options.LockTtlMilliseconds > 0
                && options.LockWaitMilliseconds > 0
                && options.LockPollMilliseconds > 0)
            .Validate(options => options.PrewarmQueueCapacity > 0
                && options.PrewarmStartupDelayMilliseconds >= 0)
            .Validate(options => options.GenerationTtlSeconds > 0)
            .Validate(options => options.UserSettings.TtlSeconds > 0)
            .Validate(options => options.Studio.SettingsTtlSeconds > 0 && options.Studio.ReferencesTtlSeconds > 0)
            .Validate(options => options.Projects.DetailTtlSeconds > 0
                && options.Projects.ListTtlSeconds > 0
                && options.Projects.MediaTtlSeconds > 0)
            .Validate(options => options.Media.TtlSeconds > 0 && options.Albums.TtlSeconds > 0)
            .Validate(options => options.StorageUsage.TtlSeconds > 0)
            .Validate(options => options.Tasks.TtlSeconds > 0 && options.Tasks.ReferencesTtlSeconds > 0)
            .Validate(options => options.Notifications.TtlSeconds > 0 && options.NotificationCount.TtlSeconds > 0)
            .Validate(options => options.EditorDocuments.TtlSeconds > 0 && options.RenderJobs.TtlSeconds > 0)
            .ValidateOnStart();
        services.Configure<MetricsOptions>(configuration.GetSection(MetricsOptions.SectionName));
        services.AddSingleton<ICacheClock, SystemCacheClock>();
        services.AddSingleton<ICacheRandom, SystemCacheRandom>();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<CachingOptions>>().Value);
        services.AddSingleton<CacheTtlJitter>();
        services.AddSingleton<CacheKeyFactory>();
        services.AddSingleton<JsonCacheCodec>();
        services.AddSingleton<BusinessCache>();
        services.AddSingleton<EditorDocumentCache>();
        services.AddSingleton<CacheGenerationManager>();
        services.AddSingleton<CachePrewarmQueue>();
        services.AddHostedService<CachePrewarmWorker>();
        services.AddSingleton<CacheCircuitBreaker>();
        services.AddSingleton<IRedisConnectionProvider, RedisConnectionProvider>();
        services.AddSingleton<DisabledCacheStore>();
        services.AddSingleton<RedisCacheStore>();
        services.AddSingleton<DisabledDistributedLockStore>();
        services.AddSingleton<RedisDistributedLockStore>();
        services.AddSingleton<ICacheStore>(sp =>
            sp.GetRequiredService<IOptions<CachingOptions>>().Value.Enabled
                ? sp.GetRequiredService<RedisCacheStore>()
                : sp.GetRequiredService<DisabledCacheStore>());
        services.AddSingleton<IDistributedLockStore>(sp =>
            sp.GetRequiredService<IOptions<CachingOptions>>().Value.Enabled
                ? sp.GetRequiredService<RedisDistributedLockStore>()
                : sp.GetRequiredService<DisabledDistributedLockStore>());
        return services;
    }
}
