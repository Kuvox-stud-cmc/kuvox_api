using Kuvox.Api.Modules.Shared.Infrastructure.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tests;

public sealed class BusinessCacheTests
{
    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task All_three_flags_are_required(bool global, bool business, bool domain)
    {
        var (cache, store, feature, _) = Create(global, business, domain);
        var calls = 0;

        var value = await cache.GetOrCreateAsync(
            "projects", "detail", feature, "key", TimeSpan.FromSeconds(30),
            _ => Task.FromResult(++calls), default);

        Assert.Equal(1, value);
        Assert.Equal(1, calls);
        Assert.Equal(0, store.Reads);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task Cold_then_warm_returns_authoritative_value_once()
    {
        var (cache, store, feature, _) = Create(true, true, true);
        var calls = 0;
        Task<Payload> Load(CancellationToken _) => Task.FromResult(new Payload(++calls));

        var cold = await cache.GetOrCreateAsync("media", "detail", feature, "key", TimeSpan.FromSeconds(20), Load);
        var warm = await cache.GetOrCreateAsync("media", "detail", feature, "key", TimeSpan.FromSeconds(20), Load);

        Assert.Equal(1, cold.Value);
        Assert.Equal(1, warm.Value);
        Assert.Equal(1, calls);
        Assert.Equal(2, store.Reads);
        Assert.Equal(1, store.Writes);
        Assert.Equal(TimeSpan.FromSeconds(20), store.LastTtl);
    }

    [Fact]
    public async Task Sixteen_concurrent_cold_reads_use_one_authoritative_execution()
    {
        var options = new CachingOptions
        {
            Enabled = true,
            StampedeProtectionEnabled = true,
            LockTtlMilliseconds = 5_000,
            LockWaitMilliseconds = 2_000,
            LockPollMilliseconds = 5,
            BusinessReads = new CacheFeatureOptions { Enabled = true },
        };
        var feature = new CacheFeatureOptions { Enabled = true, TtlSeconds = 30 };
        var store = new MemoryStore();
        var locks = new MemoryLocks();
        var cache = new BusinessCache(
            store,
            new JsonCacheCodec(new SystemCacheClock()),
            Options.Create(options),
            NullLogger<BusinessCache>.Instance,
            locks);
        var calls = 0;

        async Task<Payload> Load(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(25, cancellationToken);
            return new Payload(42);
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ =>
            cache.GetOrCreateAsync(
                "storage-usage",
                "studio-aggregate",
                feature,
                "shared-key",
                TimeSpan.FromSeconds(30),
                Load,
                useSingleFlight: true)));

        Assert.Equal(1, calls);
        Assert.All(results, result => Assert.Equal(42, result.Value));
    }

    [Fact]
    public async Task Corrupt_value_is_deleted_and_repaired()
    {
        var (cache, store, feature, _) = Create(true, true, true);
        store.Values["key"] = "not-json"u8.ToArray();

        var result = await cache.GetOrCreateAsync(
            "tasks", "detail", feature, "key", TimeSpan.FromSeconds(15),
            _ => Task.FromResult(new Payload(7)));

        Assert.Equal(7, result.Value);
        Assert.Equal(1, store.Deletes);
        Assert.Equal(1, store.Writes);
    }

    [Fact]
    public async Task Successful_but_partial_results_can_bypass_writes()
    {
        var (cache, store, feature, _) = Create(true, true, true);
        var calls = 0;

        await cache.GetOrCreateAsync(
            "media", "detail", feature, "partial", TimeSpan.FromSeconds(20),
            _ => Task.FromResult(new Payload(++calls)), default,
            result => result.Value > 1);
        await cache.GetOrCreateAsync(
            "media", "detail", feature, "partial", TimeSpan.FromSeconds(20),
            _ => Task.FromResult(new Payload(++calls)), default,
            result => result.Value > 1);

        Assert.Equal(2, calls);
        Assert.Equal(1, store.Writes);
    }

    [Fact]
    public async Task Authoritative_exceptions_and_cancellation_are_not_hidden()
    {
        var (cache, _, feature, _) = Create(true, true, true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetOrCreateAsync<Payload>(
            "projects", "detail", feature, "missing", TimeSpan.FromSeconds(30),
            _ => throw new InvalidOperationException("authoritative")));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.GetOrCreateAsync(
            "projects", "detail", feature, "cancelled", TimeSpan.FromSeconds(30),
            ct => Task.FromCanceled<Payload>(ct), cts.Token));
    }

    [Fact]
    public async Task Caller_cancellation_does_not_cancel_owned_lock_release()
    {
        var options = new CachingOptions
        {
            Enabled = true,
            StampedeProtectionEnabled = true,
            OperationTimeoutMilliseconds = 500,
            LockTtlMilliseconds = 5_000,
            LockWaitMilliseconds = 2_000,
            LockPollMilliseconds = 5,
            BusinessReads = new CacheFeatureOptions { Enabled = true },
        };
        var feature = new CacheFeatureOptions { Enabled = true, TtlSeconds = 30 };
        var store = new MemoryStore();
        var locks = new MemoryLocks();
        var cache = new BusinessCache(
            store,
            new JsonCacheCodec(new SystemCacheClock()),
            Options.Create(options),
            NullLogger<BusinessCache>.Instance,
            locks);
        using var caller = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.GetOrCreateAsync(
            "storage-usage",
            "studio-aggregate",
            feature,
            "cancelled-leader",
            TimeSpan.FromSeconds(30),
            cancellationToken =>
            {
                caller.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(new Payload(42));
            },
            caller.Token,
            useSingleFlight: true));

        Assert.False(locks.ReleaseTokenWasCancelled);
        Assert.Equal(0, locks.HeldCount);
    }

    [Theory]
    [InlineData(LockAcquireOutcome.Bypass)]
    [InlineData(LockAcquireOutcome.Error)]
    public async Task Lock_bypass_and_acquisition_error_use_authoritative_fallback(
        LockAcquireOutcome outcome)
    {
        var locks = new ScriptedLocks(outcome);
        var (cache, feature) = CreateSingleFlight(locks);
        var calls = 0;

        var result = await cache.GetOrCreateAsync(
            "storage-usage", "studio-aggregate", feature, "key", TimeSpan.FromSeconds(30),
            _ => Task.FromResult(new Payload(++calls)), useSingleFlight: true);

        Assert.Equal(1, result.Value);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Thrown_acquisition_and_release_errors_fail_open()
    {
        var acquisition = new ScriptedLocks(LockAcquireOutcome.Error) { ThrowAcquire = true };
        var (acquisitionCache, feature) = CreateSingleFlight(acquisition);
        Assert.Equal(7, (await acquisitionCache.GetOrCreateAsync(
            "storage-usage", "studio-aggregate", feature, "acquire", TimeSpan.FromSeconds(30),
            _ => Task.FromResult(new Payload(7)), useSingleFlight: true)).Value);

        var release = new ScriptedLocks(LockAcquireOutcome.Acquired) { ThrowRelease = true };
        var (releaseCache, releaseFeature) = CreateSingleFlight(release);
        Assert.Equal(8, (await releaseCache.GetOrCreateAsync(
            "storage-usage", "studio-aggregate", releaseFeature, "release", TimeSpan.FromSeconds(30),
            _ => Task.FromResult(new Payload(8)), useSingleFlight: true)).Value);
    }

    [Fact]
    public async Task Leader_failure_releases_lock_and_propagates_error()
    {
        var locks = new ScriptedLocks(LockAcquireOutcome.Acquired);
        var (cache, feature) = CreateSingleFlight(locks);

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetOrCreateAsync<Payload>(
            "storage-usage", "studio-aggregate", feature, "leader-failure", TimeSpan.FromSeconds(30),
            _ => throw new InvalidOperationException("leader"), useSingleFlight: true));

        Assert.Equal(1, locks.ReleaseCalls);
    }

    [Fact]
    public async Task Expired_lock_retries_and_wait_timeout_falls_back()
    {
        var expired = new ScriptedLocks(
            LockAcquireOutcome.Contended,
            LockAcquireOutcome.Acquired)
        {
            IsLocked = false,
        };
        var (expiredCache, feature) = CreateSingleFlight(expired);
        Assert.Equal(9, (await expiredCache.GetOrCreateAsync(
            "storage-usage", "studio-aggregate", feature, "expired", TimeSpan.FromSeconds(30),
            _ => Task.FromResult(new Payload(9)), useSingleFlight: true)).Value);

        var timeout = new ScriptedLocks(LockAcquireOutcome.Contended) { IsLocked = true };
        var (timeoutCache, timeoutFeature) = CreateSingleFlight(timeout, lockWaitMilliseconds: 5);
        Assert.Equal(10, (await timeoutCache.GetOrCreateAsync(
            "storage-usage", "studio-aggregate", timeoutFeature, "timeout", TimeSpan.FromSeconds(30),
            _ => Task.FromResult(new Payload(10)), useSingleFlight: true)).Value);
    }

    [Fact]
    public async Task Cache_outage_returns_the_same_authoritative_value_without_locking()
    {
        var locks = new ScriptedLocks(LockAcquireOutcome.Acquired);
        var (cache, feature, store) = CreateSingleFlightWithStore(locks);
        store.ReadOutcome = CacheReadOutcome.Error;

        var result = await cache.GetOrCreateAsync(
            "storage-usage", "studio-aggregate", feature, "outage", TimeSpan.FromSeconds(30),
            _ => Task.FromResult(new Payload(11)), useSingleFlight: true);

        Assert.Equal(11, result.Value);
        Assert.Equal(0, locks.AcquireCalls);
    }

    [Fact]
    public async Task Generations_are_persisted_and_bumped_without_scanning()
    {
        var (_, store, _, generations) = Create(true, true, true);
        var first = await generations.GetAsync("projects", "owner-user-1");
        var second = await generations.GetAsync("projects", "owner-user-1");
        Assert.Equal(first, second);
        Assert.NotNull(first);
        Assert.Equal(32, first!.Length);

        Assert.True(await generations.BumpAsync("projects", "owner-user-1"));
        var third = await generations.GetAsync("projects", "owner-user-1");
        Assert.NotEqual(first, third);
        Assert.DoesNotContain(store.Values.Keys, key => key.Contains("*", StringComparison.Ordinal));
    }

    [Fact]
    public void Canonical_hash_normalizes_id_order_utc_and_boolean_values()
    {
        var first = BusinessCacheKey.Hash(
            new[] { Guid.Parse("00000000-0000-0000-0000-000000000002"), Guid.Parse("00000000-0000-0000-0000-000000000001") },
            new DateTimeOffset(2026, 7, 18, 14, 0, 0, TimeSpan.FromHours(7)), true);
        var second = BusinessCacheKey.Hash(
            new[] { Guid.Parse("00000000-0000-0000-0000-000000000001"), Guid.Parse("00000000-0000-0000-0000-000000000002") },
            new DateTimeOffset(2026, 7, 18, 7, 0, 0, TimeSpan.Zero), true);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Viewer_owner_page_filter_and_include_system_are_isolated_in_keys()
    {
        var keys = new CacheKeyFactory(new CachingOptions());
        var owner = Guid.NewGuid();
        var viewer = Guid.NewGuid();
        var baseline = BusinessCacheKey.Create(
            keys, "album-list", "owner", "studio", owner, "viewer", viewer,
            "page", 1, "size", 20, "include-system", false, "filter", "abc", "gen", "token");

        Assert.NotEqual(baseline, BusinessCacheKey.Create(
            keys, "album-list", "owner", "studio", owner, "viewer", Guid.NewGuid(),
            "page", 1, "size", 20, "include-system", false, "filter", "abc", "gen", "token"));
        Assert.NotEqual(baseline, BusinessCacheKey.Create(
            keys, "album-list", "owner", "studio", Guid.NewGuid(), "viewer", viewer,
            "page", 1, "size", 20, "include-system", false, "filter", "abc", "gen", "token"));
        Assert.NotEqual(baseline, BusinessCacheKey.Create(
            keys, "album-list", "owner", "studio", owner, "viewer", viewer,
            "page", 2, "size", 20, "include-system", false, "filter", "abc", "gen", "token"));
        Assert.NotEqual(baseline, BusinessCacheKey.Create(
            keys, "album-list", "owner", "studio", owner, "viewer", viewer,
            "page", 1, "size", 20, "include-system", true, "filter", "abc", "gen", "token"));
        Assert.NotEqual(baseline, BusinessCacheKey.Create(
            keys, "album-list", "owner", "studio", owner, "viewer", viewer,
            "page", 1, "size", 20, "include-system", false, "filter", "def", "gen", "token"));
    }

    private static (BusinessCache Cache, MemoryStore Store, CacheFeatureOptions Feature, CacheGenerationManager Generations)
        Create(bool global, bool business, bool domain)
    {
        var options = new CachingOptions
        {
            Enabled = global,
            BusinessReads = new CacheFeatureOptions { Enabled = business },
            GenerationTtlSeconds = 2_592_000,
        };
        var feature = new CacheFeatureOptions { Enabled = domain, TtlSeconds = 30 };
        var store = new MemoryStore();
        var keys = new CacheKeyFactory(options);
        var codec = new JsonCacheCodec(new SystemCacheClock());
        var cache = new BusinessCache(store, codec, Options.Create(options), NullLogger<BusinessCache>.Instance);
        var generations = new CacheGenerationManager(
            store, keys, codec, Options.Create(options), NullLogger<CacheGenerationManager>.Instance);
        return (cache, store, feature, generations);
    }

    private static (BusinessCache Cache, CacheFeatureOptions Feature) CreateSingleFlight(
        IDistributedLockStore locks,
        int lockWaitMilliseconds = 50)
    {
        var (cache, feature, _) = CreateSingleFlightWithStore(locks, lockWaitMilliseconds);
        return (cache, feature);
    }

    private static (BusinessCache Cache, CacheFeatureOptions Feature, MemoryStore Store)
        CreateSingleFlightWithStore(
            IDistributedLockStore locks,
            int lockWaitMilliseconds = 50)
    {
        var options = new CachingOptions
        {
            Enabled = true,
            StampedeProtectionEnabled = true,
            OperationTimeoutMilliseconds = 100,
            LockTtlMilliseconds = 5_000,
            LockWaitMilliseconds = lockWaitMilliseconds,
            LockPollMilliseconds = 1,
            BusinessReads = new CacheFeatureOptions { Enabled = true },
        };
        var feature = new CacheFeatureOptions { Enabled = true, TtlSeconds = 30 };
        var store = new MemoryStore();
        return (
            new BusinessCache(
                store,
                new JsonCacheCodec(new SystemCacheClock()),
                Options.Create(options),
                NullLogger<BusinessCache>.Instance,
                locks),
            feature,
            store);
    }

    private sealed record Payload(int Value);

    private sealed class MemoryStore : ICacheStore
    {
        public Dictionary<string, byte[]> Values { get; } = [];
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public int Deletes { get; private set; }
        public TimeSpan LastTtl { get; private set; }
        public CacheReadOutcome? ReadOutcome { get; set; }

        public Task<CacheReadResult> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;
            if (ReadOutcome is { } outcome)
            {
                return Task.FromResult(new CacheReadResult(outcome));
            }
            return Task.FromResult(Values.TryGetValue(key, out var value)
                ? new CacheReadResult(CacheReadOutcome.Hit, value)
                : new CacheReadResult(CacheReadOutcome.Miss));
        }

        public Task<CacheWriteOutcome> SetAsync(
            string key, ReadOnlyMemory<byte> value, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Writes++;
            LastTtl = ttl;
            Values[key] = value.ToArray();
            return Task.FromResult(CacheWriteOutcome.Success);
        }

        public Task<CacheWriteOutcome> DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Deletes++;
            Values.Remove(key);
            return Task.FromResult(CacheWriteOutcome.Success);
        }
    }

    private sealed class MemoryLocks : IDistributedLockStore
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, string> _owners = [];
        public bool ReleaseTokenWasCancelled { get; private set; }
        public int HeldCount
        {
            get
            {
                lock (_gate)
                {
                    return _owners.Count;
                }
            }
        }

        public Task<LockAcquireResult> AcquireAsync(
            string domain,
            string cacheKey,
            TimeSpan ttl,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = $"lock:{domain}:{cacheKey}";
            var owner = Guid.NewGuid().ToString("N");
            lock (_gate)
            {
                if (_owners.ContainsKey(key))
                {
                    return Task.FromResult(new LockAcquireResult(
                        LockAcquireOutcome.Contended,
                        new DistributedLockHandle(key, owner)));
                }
                _owners[key] = owner;
                return Task.FromResult(new LockAcquireResult(
                    LockAcquireOutcome.Acquired,
                    new DistributedLockHandle(key, owner)));
            }
        }

        public Task<bool?> IsLockedAsync(
            string lockKey,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return Task.FromResult<bool?>(_owners.ContainsKey(lockKey));
            }
        }

        public Task<bool> ReleaseAsync(
            DistributedLockHandle handle,
            CancellationToken cancellationToken = default)
        {
            ReleaseTokenWasCancelled = cancellationToken.IsCancellationRequested;
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (!_owners.TryGetValue(handle.Key, out var owner) || owner != handle.Owner)
                {
                    return Task.FromResult(false);
                }
                _owners.Remove(handle.Key);
                return Task.FromResult(true);
            }
        }
    }

    private sealed class ScriptedLocks(params LockAcquireOutcome[] outcomes) : IDistributedLockStore
    {
        private readonly Queue<LockAcquireOutcome> _outcomes = new(outcomes);
        public bool ThrowAcquire { get; init; }
        public bool ThrowRelease { get; init; }
        public bool? IsLocked { get; init; } = true;
        public int AcquireCalls { get; private set; }
        public int ReleaseCalls { get; private set; }

        public Task<LockAcquireResult> AcquireAsync(
            string domain,
            string cacheKey,
            TimeSpan ttl,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AcquireCalls++;
            if (ThrowAcquire)
            {
                throw new InvalidOperationException("acquire");
            }
            var outcome = _outcomes.Count > 0 ? _outcomes.Dequeue() : LockAcquireOutcome.Contended;
            return Task.FromResult(new LockAcquireResult(
                outcome,
                new DistributedLockHandle($"lock:{domain}:{cacheKey}", "owner")));
        }

        public Task<bool?> IsLockedAsync(
            string lockKey,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(IsLocked);
        }

        public Task<bool> ReleaseAsync(
            DistributedLockHandle handle,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReleaseCalls++;
            if (ThrowRelease)
            {
                throw new InvalidOperationException("release");
            }
            return Task.FromResult(true);
        }
    }
}
