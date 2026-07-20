using System.Diagnostics;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Kuvox.Api.Modules.Shared.Infrastructure.Caching;

public sealed class DisabledCacheStore : ICacheStore
{
    public Task<CacheReadResult> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        CacheMetrics.RecordOperation("get", "bypass");
        return Task.FromResult(new CacheReadResult(CacheReadOutcome.Bypass));
    }

    public Task<CacheWriteOutcome> SetAsync(
        string key,
        ReadOnlyMemory<byte> value,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        CacheMetrics.RecordOperation("set", "bypass");
        return Task.FromResult(CacheWriteOutcome.Bypass);
    }

    public Task<CacheWriteOutcome> DeleteAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        CacheMetrics.RecordOperation("delete", "bypass");
        return Task.FromResult(CacheWriteOutcome.Bypass);
    }
}

public sealed class RedisCacheStore(
    IRedisConnectionProvider connections,
    IOptions<CachingOptions> options,
    CacheTtlJitter jitter,
    CacheCircuitBreaker circuit,
    ILogger<RedisCacheStore> logger) : ICacheStore
{
    private readonly CachingOptions _options = options.Value;

    public async Task<CacheReadResult> GetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        if (!circuit.AllowRequest())
        {
            return ReadResult(CacheReadOutcome.Bypass);
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            var database = await connections.GetDatabaseAsync(cancellationToken);
            var value = await database.StringGetAsync(key).WaitAsync(OperationTimeout, cancellationToken);
            RecordRedis("get", "success", started);
            circuit.RecordSuccess();
            if (value.IsNull)
            {
                return ReadResult(CacheReadOutcome.Miss);
            }

            byte[] raw = value!;
            if (raw.Length > _options.MaxPayloadBytes)
            {
                CacheMetrics.OversizedBypasses.WithLabels("api", "get").Inc();
                return ReadResult(CacheReadOutcome.Bypass);
            }

            CacheMetrics.PayloadBytes.WithLabels("api", "get").Observe(raw.Length);
            return ReadResult(CacheReadOutcome.Hit, raw);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            RecordFailure("get", started, error);
            return ReadResult(CacheReadOutcome.Error);
        }
    }

    public async Task<CacheWriteOutcome> SetAsync(
        string key,
        ReadOnlyMemory<byte> value,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        if (value.Length > _options.MaxPayloadBytes)
        {
            CacheMetrics.OversizedBypasses.WithLabels("api", "set").Inc();
            return WriteResult("set", CacheWriteOutcome.Bypass);
        }
        if (!circuit.AllowRequest())
        {
            return WriteResult("set", CacheWriteOutcome.Bypass);
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            var database = await connections.GetDatabaseAsync(cancellationToken);
            await database.StringSetAsync(key, value.ToArray(), jitter.Apply(ttl))
                .WaitAsync(OperationTimeout, cancellationToken);
            RecordRedis("set", "success", started);
            circuit.RecordSuccess();
            CacheMetrics.PayloadBytes.WithLabels("api", "set").Observe(value.Length);
            return WriteResult("set", CacheWriteOutcome.Success);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            RecordFailure("set", started, error);
            return WriteResult("set", CacheWriteOutcome.Error);
        }
    }

    public async Task<CacheWriteOutcome> DeleteAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        if (!circuit.AllowRequest())
        {
            return WriteResult("delete", CacheWriteOutcome.Bypass);
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            var database = await connections.GetDatabaseAsync(cancellationToken);
            await database.KeyDeleteAsync(key).WaitAsync(OperationTimeout, cancellationToken);
            RecordRedis("delete", "success", started);
            circuit.RecordSuccess();
            return WriteResult("delete", CacheWriteOutcome.Success);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            RecordFailure("delete", started, error);
            return WriteResult("delete", CacheWriteOutcome.Error);
        }
    }

    private TimeSpan OperationTimeout => TimeSpan.FromMilliseconds(_options.OperationTimeoutMilliseconds);

    private static CacheReadResult ReadResult(CacheReadOutcome outcome, byte[]? value = null)
    {
        CacheMetrics.RecordOperation("get", outcome.ToString().ToLowerInvariant());
        return new CacheReadResult(outcome, value);
    }

    private static CacheWriteOutcome WriteResult(string operation, CacheWriteOutcome outcome)
    {
        CacheMetrics.RecordOperation(operation, outcome.ToString().ToLowerInvariant());
        return outcome;
    }

    private static void RecordRedis(string command, string outcome, long started)
    {
        CacheMetrics.RedisCommands.WithLabels("api", command, outcome).Inc();
        CacheMetrics.RedisLatency.WithLabels("api", command)
            .Observe(Stopwatch.GetElapsedTime(started).TotalSeconds);
    }

    private void RecordFailure(string command, long started, Exception error)
    {
        RecordRedis(command, "error", started);
        circuit.RecordFailure();
        logger.LogWarning("Optional Redis cache command {Command} failed ({ErrorType}).", command, error.GetType().Name);
    }
}
