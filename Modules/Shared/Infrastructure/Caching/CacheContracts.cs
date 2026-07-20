using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kuvox.Api.Modules.Shared.Infrastructure.Caching;

public enum CacheReadOutcome
{
    Hit,
    Miss,
    Bypass,
    Error
}

public enum CacheWriteOutcome
{
    Success,
    Bypass,
    Error
}

public sealed record CacheReadResult(CacheReadOutcome Outcome, byte[]? Value = null);

public interface ICacheStore
{
    Task<CacheReadResult> GetAsync(string key, CancellationToken cancellationToken = default);
    Task<CacheWriteOutcome> SetAsync(
        string key,
        ReadOnlyMemory<byte> value,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);
    Task<CacheWriteOutcome> DeleteAsync(string key, CancellationToken cancellationToken = default);
}

public interface ICacheClock
{
    DateTimeOffset UtcNow { get; }
    long GetTimestamp();
    TimeSpan GetElapsedTime(long startingTimestamp);
}

public sealed class SystemCacheClock : ICacheClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public long GetTimestamp() => TimeProvider.System.GetTimestamp();
    public TimeSpan GetElapsedTime(long startingTimestamp) =>
        TimeProvider.System.GetElapsedTime(startingTimestamp);
}

public interface ICacheRandom
{
    double NextDouble();
}

public sealed class SystemCacheRandom : ICacheRandom
{
    public double NextDouble() => Random.Shared.NextDouble();
}

public sealed class CacheTtlJitter(ICacheRandom random, CachingOptions options)
{
    public TimeSpan Apply(TimeSpan ttl)
    {
        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), "TTL must be positive.");
        }

        var fraction = Math.Clamp(options.TtlJitterPercent, 0, 100) / 100d;
        var multiplier = 1d - fraction + (random.NextDouble() * fraction * 2d);
        return TimeSpan.FromSeconds(Math.Max(1, Math.Round(ttl.TotalSeconds * multiplier)));
    }
}

public sealed class CacheKeyFactory(CachingOptions options)
{
    public string Create(params string[] parts)
    {
        var prefix = options.KeyPrefix.Trim(':');
        var normalized = parts.Select(part => part.Trim(':')).ToArray();
        if (string.IsNullOrWhiteSpace(prefix) || normalized.Length == 0
            || normalized.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Cache key prefix and parts must be non-empty.", nameof(parts));
        }

        return string.Join(':', [prefix, .. normalized]);
    }

    public static string Sha256(string alreadyCanonicalSensitiveInput) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(alreadyCanonicalSensitiveInput)));
}

public sealed class JsonCacheCodec(ICacheClock clock)
{
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public byte[] Encode<T>(T payload) => JsonSerializer.SerializeToUtf8Bytes(
        new CacheEnvelope<T>(SchemaVersion, clock.UtcNow, payload),
        SerializerOptions);

    public bool TryDecode<T>(ReadOnlySpan<byte> value, out T? payload)
    {
        payload = default;
        try
        {
            var envelope = JsonSerializer.Deserialize<CacheEnvelope<T>>(value, SerializerOptions);
            if (envelope is null || envelope.SchemaVersion != SchemaVersion)
            {
                CacheMetrics.RecordSchemaMiss();
                return false;
            }
            if (envelope.CreatedAtUtc == default || envelope.Payload is null)
            {
                return false;
            }

            payload = envelope.Payload;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record CacheEnvelope<T>(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("created_at_utc")] DateTimeOffset CreatedAtUtc,
        [property: JsonPropertyName("payload")] T Payload);
}
