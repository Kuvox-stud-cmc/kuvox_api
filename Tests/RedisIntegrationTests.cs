using Kuvox.Api.Modules.Shared.Infrastructure.Caching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tests;

public sealed class RedisIntegrationTests
{
    [Fact]
    public async Task Real_redis_supports_raw_json_ttl_delete_and_connection_reuse()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable("KUVOX_RUN_REDIS_INTEGRATION"),
            "true",
            StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Redis"] =
                    Environment.GetEnvironmentVariable("KUVOX_TEST_REDIS_CONNECTION")
                    ?? "localhost:6379,defaultDatabase=15"
            })
            .Build();
        var options = new CachingOptions
        {
            Enabled = true,
            StampedeProtectionEnabled = true,
            BusinessReads = new CacheFeatureOptions { Enabled = true },
            ConnectTimeoutMilliseconds = 500,
            OperationTimeoutMilliseconds = 500,
            TtlJitterPercent = 0
        };
        await using var provider = new RedisConnectionProvider(
            configuration,
            Options.Create(options),
            NullLogger<RedisConnectionProvider>.Instance);
        var first = await provider.GetDatabaseAsync();
        var second = await provider.GetDatabaseAsync();
        Assert.Same(first.Multiplexer, second.Multiplexer);

        var store = new RedisCacheStore(
            provider,
            Options.Create(options),
            new CacheTtlJitter(new FixedRandom(), options),
            new CacheCircuitBreaker(new SystemCacheClock()),
            NullLogger<RedisCacheStore>.Instance);
        var key = $"kuvox:v1:api:test:{Guid.NewGuid():N}";
        var generationScope = $"integration-{Guid.NewGuid():N}";
        var generations = new CacheGenerationManager(
            store,
            new CacheKeyFactory(options),
            new JsonCacheCodec(new SystemCacheClock()),
            Options.Create(options),
            NullLogger<CacheGenerationManager>.Instance);
        var generationKey = generations.GenerationKey("test", generationScope);
        var keys = new CacheKeyFactory(options);
        var locks = new RedisDistributedLockStore(
            provider,
            keys,
            Options.Create(options),
            NullLogger<RedisDistributedLockStore>.Instance);
        try
        {
            Assert.Equal(CacheWriteOutcome.Success, await store.SetAsync(key, "raw\0bytes"u8.ToArray(), TimeSpan.FromSeconds(1)));
            Assert.Equal("raw\0bytes"u8.ToArray(), (await store.GetAsync(key)).Value);
            await Task.Delay(TimeSpan.FromMilliseconds(1100));
            Assert.Equal(CacheReadOutcome.Miss, (await store.GetAsync(key)).Outcome);

            var codec = new JsonCacheCodec(new SystemCacheClock());
            Assert.Equal(CacheWriteOutcome.Success, await store.SetAsync(key, codec.Encode(new Payload(true)), TimeSpan.FromSeconds(30)));
            var json = await store.GetAsync(key);
            Assert.True(codec.TryDecode<Payload>(json.Value!, out var payload));
            Assert.True(payload?.Ok);
            Assert.Equal(CacheWriteOutcome.Success, await store.DeleteAsync(key));
            Assert.Equal(CacheReadOutcome.Miss, (await store.GetAsync(key)).Outcome);

            var firstGeneration = await generations.GetAsync("test", generationScope);
            Assert.NotNull(firstGeneration);
            Assert.True(await generations.BumpAsync("test", generationScope));
            var secondGeneration = await generations.GetAsync("test", generationScope);
            Assert.NotEqual(firstGeneration, secondGeneration);

            var acquired = await locks.AcquireAsync("storage-usage", key, TimeSpan.FromSeconds(5));
            Assert.Equal(LockAcquireOutcome.Acquired, acquired.Outcome);
            Assert.NotNull(acquired.Handle);
            var database = await provider.GetDatabaseAsync();
            var lockTtl = await database.KeyTimeToLiveAsync(acquired.Handle!.Key);
            Assert.InRange(lockTtl!.Value.TotalMilliseconds, 4_000, 5_000);
            Assert.False(await locks.ReleaseAsync(new DistributedLockHandle(
                acquired.Handle.Key, Guid.NewGuid().ToString("N"))));
            Assert.True(await locks.ReleaseAsync(acquired.Handle));

            var feature = new CacheFeatureOptions { Enabled = true, TtlSeconds = 30 };
            var cacheOne = new BusinessCache(
                store,
                new JsonCacheCodec(new SystemCacheClock()),
                Options.Create(options),
                NullLogger<BusinessCache>.Instance,
                locks);
            var cacheTwo = new BusinessCache(
                store,
                new JsonCacheCodec(new SystemCacheClock()),
                Options.Create(options),
                NullLogger<BusinessCache>.Instance,
                locks);
            var calls = 0;
            async Task<Payload> Load(CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref calls);
                await Task.Delay(50, cancellationToken);
                return new Payload(true);
            }
            var sharedKey = $"kuvox:v1:api:integration-single-flight:{Guid.NewGuid():N}";
            var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(index =>
                (index % 2 == 0 ? cacheOne : cacheTwo).GetOrCreateAsync(
                    "storage-usage",
                    "studio-aggregate",
                    feature,
                    sharedKey,
                    TimeSpan.FromSeconds(30),
                    Load,
                    useSingleFlight: true)));
            Assert.Equal(1, calls);
            Assert.All(results, result => Assert.True(result.Ok));
            await store.DeleteAsync(sharedKey);
        }
        finally
        {
            await store.DeleteAsync(key);
            await store.DeleteAsync(generationKey);
        }
    }

    private sealed record Payload(bool Ok);

    private sealed class FixedRandom : ICacheRandom
    {
        public double NextDouble() => 0.5;
    }
}
