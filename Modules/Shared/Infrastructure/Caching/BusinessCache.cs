using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Kuvox.Api.Modules.Shared.Infrastructure.Caching;

public sealed class BusinessCache(
    ICacheStore store,
    JsonCacheCodec codec,
    IOptions<CachingOptions> options,
    ILogger<BusinessCache> logger,
    IDistributedLockStore? lockStore = null)
{
    private readonly CachingOptions _options = options.Value;
    private readonly IDistributedLockStore _locks = lockStore ?? new DisabledDistributedLockStore();

    public bool IsEnabled(CacheFeatureOptions feature) =>
        _options.Enabled && _options.BusinessReads.Enabled && feature.Enabled;

    public async Task<T> GetOrCreateAsync<T>(
        string domain,
        string operation,
        CacheFeatureOptions feature,
        string key,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken = default,
        Func<T, bool>? shouldCache = null,
        bool useSingleFlight = false)
    {
        if (!IsEnabled(feature))
        {
            Record(domain, operation, "disabled", 0);
            return await ExecuteAuthoritativeAsync(domain, factory, cancellationToken);
        }

        var started = Stopwatch.GetTimestamp();
        var coordinationAvailable = true;
        try
        {
            var read = await store.GetAsync(key, cancellationToken);
            if (read.Outcome == CacheReadOutcome.Hit && read.Value is { } value)
            {
                CacheMetrics.BusinessPayloadBytes.WithLabels(domain, "read").Observe(value.Length);
                if (codec.TryDecode<T>(value, out var cached) && cached is not null)
                {
                    Record(domain, operation, "hit", started);
                    return cached;
                }

                var repair = await store.DeleteAsync(key, cancellationToken);
                CacheMetrics.BusinessInvalidations
                    .WithLabels(domain, "corrupt", Outcome(repair)).Inc();
                Record(domain, operation, "corrupt", started);
            }
            else
            {
                Record(domain, operation, read.Outcome.ToString().ToLowerInvariant(), started);
                coordinationAvailable = read.Outcome is not (
                    CacheReadOutcome.Error or CacheReadOutcome.Bypass);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            Record(domain, operation, "error", started);
            coordinationAvailable = false;
            logger.LogWarning(
                "Optional business cache read failed for domain {Domain} ({ErrorType}).",
                domain,
                error.GetType().Name);
        }

        if (useSingleFlight && _options.StampedeProtectionEnabled && coordinationAvailable)
        {
            return await GetOrCreateSingleFlightAsync(
                domain, operation, feature, key, ttl, factory, cancellationToken, shouldCache);
        }

        return await ComputeAndCacheAsync(
            domain, feature, key, ttl, factory, cancellationToken, shouldCache);
    }

    public async Task WriteAsync<T>(
        string domain,
        CacheFeatureOptions feature,
        string key,
        TimeSpan ttl,
        T value,
        CancellationToken cancellationToken = default) =>
        _ = await TryWriteAsync(domain, feature, key, ttl, value, cancellationToken);

    public async Task<bool> TryWriteAsync<T>(
        string domain,
        CacheFeatureOptions feature,
        string key,
        TimeSpan ttl,
        T value,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled(feature))
        {
            CacheMetrics.BusinessOperations.WithLabels(domain, "write", "disabled").Inc();
            return false;
        }

        try
        {
            var encoded = codec.Encode(value);
            CacheMetrics.BusinessPayloadBytes.WithLabels(domain, "write").Observe(encoded.Length);
            if (encoded.Length > _options.MaxPayloadBytes)
            {
                CacheMetrics.BusinessOperations.WithLabels(domain, "write", "oversized").Inc();
                return false;
            }

            var write = await store.SetAsync(key, encoded, ttl, cancellationToken);
            CacheMetrics.BusinessOperations.WithLabels(domain, "write", Outcome(write)).Inc();
            return write == CacheWriteOutcome.Success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            CacheMetrics.BusinessOperations.WithLabels(domain, "write", "error").Inc();
            logger.LogWarning(
                "Optional business cache write failed for domain {Domain} ({ErrorType}).",
                domain,
                error.GetType().Name);
            return false;
        }
    }

    public async Task InvalidateExactAsync(
        string domain,
        string key,
        CacheFeatureOptions? feature = null,
        CancellationToken cancellationToken = default)
    {
        if (feature is not null && !IsEnabled(feature))
        {
            CacheMetrics.BusinessInvalidations.WithLabels(domain, "exact", "disabled").Inc();
            return;
        }

        try
        {
            var outcome = await store.DeleteAsync(key, cancellationToken);
            CacheMetrics.BusinessInvalidations.WithLabels(domain, "exact", Outcome(outcome)).Inc();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            CacheMetrics.BusinessInvalidations.WithLabels(domain, "exact", "error").Inc();
            logger.LogWarning(
                "Best-effort business cache invalidation failed for domain {Domain} ({ErrorType}).",
                domain,
                error.GetType().Name);
        }
    }

    private static void Record(string domain, string operation, string outcome, long started)
    {
        CacheMetrics.BusinessOperations.WithLabels(domain, operation, outcome).Inc();
        if (started != 0)
        {
            CacheMetrics.BusinessDuration.WithLabels(domain, operation)
                .Observe(Stopwatch.GetElapsedTime(started).TotalSeconds);
        }
    }

    private static async Task<T> ExecuteAuthoritativeAsync<T>(
        string domain,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await factory(cancellationToken);
            Record(domain, "authoritative", "success", started);
            return result;
        }
        catch
        {
            Record(domain, "authoritative", "error", started);
            throw;
        }
    }

    private async Task<T> GetOrCreateSingleFlightAsync<T>(
        string domain,
        string operation,
        CacheFeatureOptions feature,
        string key,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken,
        Func<T, bool>? shouldCache)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(_options.LockWaitMilliseconds);
        while (true)
        {
            LockAcquireResult attempt;
            try
            {
                attempt = await _locks.AcquireAsync(
                    domain,
                    key,
                    TimeSpan.FromMilliseconds(_options.LockTtlMilliseconds),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                attempt = new LockAcquireResult(LockAcquireOutcome.Error);
            }
            if (attempt.Outcome == LockAcquireOutcome.Acquired && attempt.Handle is not null)
            {
                CacheMetrics.SingleFlightEvents.WithLabels("api", domain, "leader").Inc();
                CacheMetrics.SingleFlightHeldLocks.WithLabels("api", domain).Inc();
                try
                {
                    var raced = await TryReadJoinedAsync<T>(domain, operation, key, cancellationToken);
                    if (raced.Outcome == JoinedReadOutcome.Hit)
                    {
                        CacheMetrics.SingleFlightEvents
                            .WithLabels("api", domain, "joined_cache_hit").Inc();
                        return raced.Value!;
                    }
                    return await ComputeAndCacheAsync(
                        domain, feature, key, ttl, factory, cancellationToken, shouldCache);
                }
                finally
                {
                    try
                    {
                        using var releaseTimeout = new CancellationTokenSource(
                            TimeSpan.FromMilliseconds(_options.OperationTimeoutMilliseconds));
                        if (!await _locks.ReleaseAsync(attempt.Handle, releaseTimeout.Token))
                        {
                            CacheMetrics.SingleFlightEvents
                                .WithLabels("api", domain, "release_error").Inc();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        CacheMetrics.SingleFlightEvents
                            .WithLabels("api", domain, "release_error").Inc();
                    }
                    catch
                    {
                        CacheMetrics.SingleFlightEvents
                            .WithLabels("api", domain, "release_error").Inc();
                    }
                    finally
                    {
                        CacheMetrics.SingleFlightHeldLocks.WithLabels("api", domain).Dec();
                    }
                }
            }

            if (attempt.Outcome is LockAcquireOutcome.Bypass or LockAcquireOutcome.Error)
            {
                CacheMetrics.SingleFlightEvents.WithLabels(
                    "api", domain,
                    attempt.Outcome == LockAcquireOutcome.Bypass ? "bypass" : "acquisition_error").Inc();
                CacheMetrics.SingleFlightEvents
                    .WithLabels("api", domain, "authoritative_fallback").Inc();
                return await ComputeAndCacheAsync(
                    domain, feature, key, ttl, factory, cancellationToken, shouldCache);
            }

            CacheMetrics.SingleFlightEvents.WithLabels("api", domain, "join").Inc();
            var waitStarted = Stopwatch.GetTimestamp();
            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(_options.LockPollMilliseconds, cancellationToken);
                var joined = await TryReadJoinedAsync<T>(domain, operation, key, cancellationToken);
                if (joined.Outcome == JoinedReadOutcome.Hit)
                {
                    CacheMetrics.SingleFlightEvents
                        .WithLabels("api", domain, "joined_cache_hit").Inc();
                    CacheMetrics.SingleFlightWait.WithLabels("api", domain)
                        .Observe(Stopwatch.GetElapsedTime(waitStarted).TotalSeconds);
                    return joined.Value!;
                }
                if (joined.Outcome == JoinedReadOutcome.Failure)
                {
                    CacheMetrics.SingleFlightEvents.WithLabels("api", domain, "bypass").Inc();
                    CacheMetrics.SingleFlightEvents
                        .WithLabels("api", domain, "authoritative_fallback").Inc();
                    CacheMetrics.SingleFlightWait.WithLabels("api", domain)
                        .Observe(Stopwatch.GetElapsedTime(waitStarted).TotalSeconds);
                    return await ComputeAndCacheAsync(
                        domain, feature, key, ttl, factory, cancellationToken, shouldCache);
                }

                bool? locked;
                try
                {
                    locked = attempt.Handle is null
                        ? false
                        : await _locks.IsLockedAsync(attempt.Handle.Key, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    locked = null;
                }
                if (locked is null)
                {
                    CacheMetrics.SingleFlightEvents.WithLabels("api", domain, "bypass").Inc();
                    CacheMetrics.SingleFlightEvents
                        .WithLabels("api", domain, "authoritative_fallback").Inc();
                    return await ComputeAndCacheAsync(
                        domain, feature, key, ttl, factory, cancellationToken, shouldCache);
                }
                if (!locked.Value)
                {
                    break;
                }
            }

            CacheMetrics.SingleFlightWait.WithLabels("api", domain)
                .Observe(Stopwatch.GetElapsedTime(waitStarted).TotalSeconds);
            if (DateTimeOffset.UtcNow >= deadline)
            {
                CacheMetrics.SingleFlightEvents.WithLabels("api", domain, "timeout").Inc();
                CacheMetrics.SingleFlightEvents
                    .WithLabels("api", domain, "authoritative_fallback").Inc();
                return await ComputeAndCacheAsync(
                    domain, feature, key, ttl, factory, cancellationToken, shouldCache);
            }
        }
    }

    private async Task<T> ComputeAndCacheAsync<T>(
        string domain,
        CacheFeatureOptions feature,
        string key,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken,
        Func<T, bool>? shouldCache)
    {
        // The source-of-truth call deliberately sits outside cache exception handling:
        // authorization, validation, conflicts, and not-found results remain authoritative.
        var result = await ExecuteAuthoritativeAsync(domain, factory, cancellationToken);
        if (result is null || shouldCache is not null && !shouldCache(result))
        {
            return result!;
        }

        await WriteAsync(domain, feature, key, ttl, result, cancellationToken);
        return result;
    }

    private async Task<JoinedRead<T>> TryReadJoinedAsync<T>(
        string domain,
        string operation,
        string key,
        CancellationToken cancellationToken)
    {
        try
        {
            var read = await store.GetAsync(key, cancellationToken);
            if (read.Outcome == CacheReadOutcome.Hit && read.Value is { } value)
            {
                CacheMetrics.BusinessPayloadBytes.WithLabels(domain, "read").Observe(value.Length);
                if (codec.TryDecode<T>(value, out var cached) && cached is not null)
                {
                    Record(domain, operation, "hit", 0);
                    return new JoinedRead<T>(JoinedReadOutcome.Hit, cached);
                }
                await store.DeleteAsync(key, cancellationToken);
                return new JoinedRead<T>(JoinedReadOutcome.Miss);
            }
            return read.Outcome is CacheReadOutcome.Error or CacheReadOutcome.Bypass
                ? new JoinedRead<T>(JoinedReadOutcome.Failure)
                : new JoinedRead<T>(JoinedReadOutcome.Miss);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            logger.LogWarning(
                "Optional business single-flight poll failed for domain {Domain} ({ErrorType}).",
                domain,
                error.GetType().Name);
            return new JoinedRead<T>(JoinedReadOutcome.Failure);
        }
    }

    private enum JoinedReadOutcome
    {
        Hit,
        Miss,
        Failure
    }

    private sealed record JoinedRead<T>(JoinedReadOutcome Outcome, T? Value = default);

    private static string Outcome(CacheWriteOutcome outcome) => outcome.ToString().ToLowerInvariant();
}

public sealed class CacheGenerationManager(
    ICacheStore store,
    CacheKeyFactory keys,
    JsonCacheCodec codec,
    IOptions<CachingOptions> options,
    ILogger<CacheGenerationManager> logger)
{
    private readonly CachingOptions _options = options.Value;

    public async Task<string?> GetAsync(
        string domain,
        string scope,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || !_options.BusinessReads.Enabled)
        {
            return null;
        }

        var key = GenerationKey(domain, scope);
        var read = await store.GetAsync(key, cancellationToken);
        if (read.Outcome == CacheReadOutcome.Hit
            && read.Value is { } raw
            && codec.TryDecode<string>(raw, out var existing)
            && IsToken(existing))
        {
            CacheMetrics.BusinessGenerationOperations.WithLabels(domain, "get", "hit").Inc();
            return existing;
        }

        if (read.Outcome is CacheReadOutcome.Error or CacheReadOutcome.Bypass)
        {
            CacheMetrics.BusinessGenerationOperations
                .WithLabels(domain, "get", read.Outcome.ToString().ToLowerInvariant()).Inc();
            return null;
        }

        return await PersistNewAsync(domain, key, "create", cancellationToken);
    }

    public async Task<bool> BumpAsync(
        string domain,
        string scope,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || !_options.BusinessReads.Enabled)
        {
            CacheMetrics.BusinessInvalidations.WithLabels(domain, "generation", "disabled").Inc();
            return false;
        }

        var token = await PersistNewAsync(domain, GenerationKey(domain, scope), "bump", cancellationToken);
        CacheMetrics.BusinessInvalidations
            .WithLabels(domain, "generation", token is null ? "error" : "success").Inc();
        return token is not null;
    }

    public string GenerationKey(string domain, string scope) =>
        keys.Create("api", "gen", CanonicalPart(domain), CanonicalPart(scope));

    private async Task<string?> PersistNewAsync(
        string domain,
        string key,
        string operation,
        CancellationToken cancellationToken)
    {
        try
        {
            var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
            var outcome = await store.SetAsync(
                key,
                codec.Encode(token),
                TimeSpan.FromSeconds(_options.GenerationTtlSeconds),
                cancellationToken);
            CacheMetrics.BusinessGenerationOperations.WithLabels(domain, operation, Outcome(outcome)).Inc();
            return outcome == CacheWriteOutcome.Success ? token : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            CacheMetrics.BusinessGenerationOperations.WithLabels(domain, operation, "error").Inc();
            logger.LogWarning(
                "Best-effort cache generation operation failed for domain {Domain} ({ErrorType}).",
                domain,
                error.GetType().Name);
            return null;
        }
    }

    private static bool IsToken(string? token) => token is { Length: 32 } && token.All(Uri.IsHexDigit);
    private static string Outcome(CacheWriteOutcome outcome) => outcome.ToString().ToLowerInvariant();
    private static string CanonicalPart(string value) => value.Trim().ToLowerInvariant().Replace(':', '-');
}

public static class BusinessCacheKey
{
    public static string Create(CacheKeyFactory keys, params object?[] parts)
    {
        var canonical = parts.Select(Canonical).ToArray();
        return keys.Create(["api", .. canonical, "schema", "1"]);
    }

    public static string Hash(params object?[] values) =>
        CacheKeyFactory.Sha256(string.Join('|', values.Select(Canonical)));

    public static string Canonical(object? value) => value switch
    {
        null => "none",
        bool boolean => boolean ? "true" : "false",
        Guid guid => guid.ToString("N"),
        DateTimeOffset timestamp => timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        DateTime timestamp => timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        Enum enumeration => enumeration.ToString().ToLowerInvariant(),
        IEnumerable<Guid> ids => string.Join(',', ids.Order().Select(id => id.ToString("N"))),
        string text => text.Trim().ToLowerInvariant().Replace(':', '-'),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "none",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim().ToLowerInvariant() ?? "none"
    };
}
