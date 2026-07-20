using System.Reflection;
using Kuvox.Api.Modules.Shared.Infrastructure.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;

namespace Tests;

public sealed class CacheStoreTests
{
    [Fact]
    public async Task Redis_store_supports_hit_miss_write_delete_and_payload_limits()
    {
        var database = DatabaseProxy.Create(out var handler);
        var store = CreateStore(database, maxPayloadBytes: 4);
        Assert.Equal(CacheReadOutcome.Miss, (await store.GetAsync("missing")).Outcome);
        Assert.Equal(CacheWriteOutcome.Success, await store.SetAsync("key", "data"u8.ToArray(), TimeSpan.FromSeconds(10)));
        var hit = await store.GetAsync("key");
        Assert.Equal(CacheReadOutcome.Hit, hit.Outcome);
        Assert.Equal("data"u8.ToArray(), hit.Value);
        Assert.Equal(CacheWriteOutcome.Bypass, await store.SetAsync("large", "12345"u8.ToArray(), TimeSpan.FromSeconds(10)));
        handler.Values["oversized"] = "12345"u8.ToArray();
        Assert.Equal(CacheReadOutcome.Bypass, (await store.GetAsync("oversized")).Outcome);
        Assert.Equal(CacheWriteOutcome.Success, await store.DeleteAsync("key"));
        Assert.Equal(CacheReadOutcome.Miss, (await store.GetAsync("key")).Outcome);
    }

    [Fact]
    public async Task Redis_failures_return_error_and_open_the_circuit()
    {
        var clock = new FakeClock();
        var provider = new FakeProvider { Failure = new RedisConnectionException(ConnectionFailureType.UnableToConnect, "offline") };
        var store = CreateStore(provider, clock: clock);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.Equal(CacheReadOutcome.Error, (await store.GetAsync("key")).Outcome);
        }
        var calls = provider.Calls;
        Assert.Equal(CacheReadOutcome.Bypass, (await store.GetAsync("key")).Outcome);
        Assert.Equal(calls, provider.Calls);
        clock.Advance(TimeSpan.FromSeconds(10));
        provider.Failure = null;
        provider.Database = DatabaseProxy.Create(out _);
        Assert.Equal(CacheReadOutcome.Miss, (await store.GetAsync("key")).Outcome);
    }

    [Fact]
    public async Task Explicit_request_cancellation_propagates()
    {
        var database = DatabaseProxy.Create(out var handler);
        handler.BlockReads = true;
        var store = CreateStore(database, operationTimeoutMilliseconds: 10_000);
        using var cancellation = new CancellationTokenSource();
        var task = store.GetAsync("key", cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    private static RedisCacheStore CreateStore(
        IDatabase database,
        int maxPayloadBytes = 1_048_576,
        int operationTimeoutMilliseconds = 500,
        FakeClock? clock = null) =>
        CreateStore(
            new FakeProvider { Database = database },
            maxPayloadBytes,
            operationTimeoutMilliseconds,
            clock);

    private static RedisCacheStore CreateStore(
        FakeProvider provider,
        int maxPayloadBytes = 1_048_576,
        int operationTimeoutMilliseconds = 500,
        FakeClock? clock = null)
    {
        var options = new CachingOptions
        {
            Enabled = true,
            MaxPayloadBytes = maxPayloadBytes,
            OperationTimeoutMilliseconds = operationTimeoutMilliseconds,
            TtlJitterPercent = 0
        };
        var actualClock = clock ?? new FakeClock();
        return new RedisCacheStore(
            provider,
            Options.Create(options),
            new CacheTtlJitter(new FixedRandom(), options),
            new CacheCircuitBreaker(actualClock),
            NullLogger<RedisCacheStore>.Instance);
    }

    private sealed class FakeProvider : IRedisConnectionProvider
    {
        public IDatabase? Database { get; set; }
        public Exception? Failure { get; set; }
        public int Calls { get; private set; }

        public Task<IDatabase> GetDatabaseAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Failure is not null) return Task.FromException<IDatabase>(Failure);
            return Task.FromResult(Database ?? throw new InvalidOperationException());
        }

        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Failure is null);
    }

    private class DatabaseProxy : DispatchProxy
    {
        public Dictionary<string, byte[]> Values { get; } = [];
        public bool BlockReads { get; set; }

        public static IDatabase Create(out DatabaseProxy handler)
        {
            var database = Create<IDatabase, DatabaseProxy>();
            handler = (DatabaseProxy)(object)database;
            return database;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var name = targetMethod?.Name;
            if (name == nameof(IDatabase.StringGetAsync))
            {
                if (BlockReads) return new TaskCompletionSource<RedisValue>().Task;
                var key = args![0]!.ToString()!;
                return Task.FromResult(Values.TryGetValue(key, out var value) ? (RedisValue)value : RedisValue.Null);
            }
            if (name == nameof(IDatabase.StringSetAsync))
            {
                Values[args![0]!.ToString()!] = (byte[]?)(RedisValue)args[1]! ?? [];
                return Task.FromResult(true);
            }
            if (name == nameof(IDatabase.KeyDeleteAsync))
            {
                return Task.FromResult(Values.Remove(args![0]!.ToString()!));
            }
            if (name == "get_Multiplexer") return null;
            throw new NotSupportedException(name);
        }
    }

    private sealed class FixedRandom : ICacheRandom
    {
        public double NextDouble() => 0.5;
    }

    private sealed class FakeClock : ICacheClock
    {
        private long _ticks;
        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch.AddTicks(_ticks);
        public long GetTimestamp() => _ticks;
        public TimeSpan GetElapsedTime(long startingTimestamp) => TimeSpan.FromTicks(_ticks - startingTimestamp);
        public void Advance(TimeSpan duration) => _ticks += duration.Ticks;
    }
}
