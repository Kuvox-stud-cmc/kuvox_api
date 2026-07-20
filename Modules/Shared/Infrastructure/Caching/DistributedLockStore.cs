using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Kuvox.Api.Modules.Shared.Infrastructure.Caching;

public enum LockAcquireOutcome
{
    Acquired,
    Contended,
    Bypass,
    Error
}

public sealed record DistributedLockHandle(string Key, string Owner);

public sealed record LockAcquireResult(LockAcquireOutcome Outcome, DistributedLockHandle? Handle = null);

public interface IDistributedLockStore
{
    Task<LockAcquireResult> AcquireAsync(
        string domain,
        string cacheKey,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);
    Task<bool?> IsLockedAsync(string lockKey, CancellationToken cancellationToken = default);
    Task<bool> ReleaseAsync(DistributedLockHandle handle, CancellationToken cancellationToken = default);
}

public sealed class DisabledDistributedLockStore : IDistributedLockStore
{
    public Task<LockAcquireResult> AcquireAsync(
        string domain, string cacheKey, TimeSpan ttl, CancellationToken cancellationToken = default) =>
        Task.FromResult(new LockAcquireResult(LockAcquireOutcome.Bypass));

    public Task<bool?> IsLockedAsync(string lockKey, CancellationToken cancellationToken = default) =>
        Task.FromResult<bool?>(null);

    public Task<bool> ReleaseAsync(DistributedLockHandle handle, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

public sealed class RedisDistributedLockStore(
    IRedisConnectionProvider connections,
    CacheKeyFactory keys,
    IOptions<CachingOptions> options,
    ILogger<RedisDistributedLockStore> logger) : IDistributedLockStore
{
    private const string ReleaseScript = """
        if redis.call('get', KEYS[1]) == ARGV[1] then
          return redis.call('del', KEYS[1])
        end
        return 0
        """;
    private readonly CachingOptions _options = options.Value;

    public async Task<LockAcquireResult> AcquireAsync(
        string domain,
        string cacheKey,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || !_options.StampedeProtectionEnabled)
        {
            return new LockAcquireResult(LockAcquireOutcome.Bypass);
        }

        var lockKey = keys.Create("api", "lock", domain, CacheKeyFactory.Sha256(cacheKey));
        var owner = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var started = Stopwatch.GetTimestamp();
        try
        {
            var database = await connections.GetDatabaseAsync(cancellationToken);
            var acquired = await database.StringSetAsync(lockKey, owner, ttl, When.NotExists)
                .WaitAsync(OperationTimeout, cancellationToken);
            Record("set_nx_px", "success", started);
            return new LockAcquireResult(
                acquired ? LockAcquireOutcome.Acquired : LockAcquireOutcome.Contended,
                new DistributedLockHandle(lockKey, owner));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            RecordFailure("set_nx_px", started, error);
            return new LockAcquireResult(LockAcquireOutcome.Error);
        }
    }

    public async Task<bool?> IsLockedAsync(
        string lockKey,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var database = await connections.GetDatabaseAsync(cancellationToken);
            var exists = await database.KeyExistsAsync(lockKey)
                .WaitAsync(OperationTimeout, cancellationToken);
            Record("exists", "success", started);
            return exists;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            RecordFailure("exists", started, error);
            return null;
        }
    }

    public async Task<bool> ReleaseAsync(
        DistributedLockHandle handle,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var database = await connections.GetDatabaseAsync(cancellationToken);
            var result = await database.ScriptEvaluateAsync(
                    ReleaseScript,
                    [new RedisKey(handle.Key)],
                    [new RedisValue(handle.Owner)])
                .WaitAsync(OperationTimeout, cancellationToken);
            Record("eval_release", "success", started);
            return (long)result == 1;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            RecordFailure("eval_release", started, error);
            return false;
        }
    }

    private TimeSpan OperationTimeout => TimeSpan.FromMilliseconds(_options.OperationTimeoutMilliseconds);

    private static void Record(string command, string outcome, long started)
    {
        CacheMetrics.RedisCommands.WithLabels("api", command, outcome).Inc();
        CacheMetrics.RedisLatency.WithLabels("api", command)
            .Observe(Stopwatch.GetElapsedTime(started).TotalSeconds);
    }

    private void RecordFailure(string command, long started, Exception error)
    {
        Record(command, "error", started);
        logger.LogWarning(
            "Optional Redis single-flight command {Command} failed ({ErrorType}).",
            command,
            error.GetType().Name);
    }
}
