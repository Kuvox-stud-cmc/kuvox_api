using System.Text.Json;
using Kuvox.Api.Modules.Shared.Infrastructure.Caching;
using Xunit;

namespace Tests;

public sealed class CacheFoundationTests
{
    [Fact]
    public async Task Disabled_store_always_bypasses()
    {
        var store = new DisabledCacheStore();
        Assert.Equal(CacheReadOutcome.Bypass, (await store.GetAsync("key")).Outcome);
        Assert.Equal(CacheWriteOutcome.Bypass, await store.SetAsync("key", "value"u8.ToArray(), TimeSpan.FromSeconds(10)));
        Assert.Equal(CacheWriteOutcome.Bypass, await store.DeleteAsync("key"));
    }

    [Fact]
    public void Contract_fixture_matches_key_hash_and_envelope_fields()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "cache_contract.json")));
        var root = fixture.RootElement;
        var options = new CachingOptions { KeyPrefix = root.GetProperty("prefix").GetString()! };
        var factory = new CacheKeyFactory(options);
        var parts = root.GetProperty("parts").EnumerateArray().Select(item => item.GetString()!).ToArray();
        Assert.Equal(root.GetProperty("key").GetString(), factory.Create(parts));
        Assert.Equal(
            root.GetProperty("sha256").GetString(),
            CacheKeyFactory.Sha256(root.GetProperty("canonical_sensitive_input").GetString()!));

        var codec = new JsonCacheCodec(new FakeClock());
        using var envelope = JsonDocument.Parse(codec.Encode(new { ok = true }));
        Assert.Equal(
            root.GetProperty("envelope_fields").EnumerateArray().Select(item => item.GetString()),
            envelope.RootElement.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public void Json_codec_accepts_version_one_and_rejects_unknown_or_invalid_values()
    {
        var codec = new JsonCacheCodec(new FakeClock());
        var encoded = codec.Encode(new Payload(true));
        Assert.True(codec.TryDecode<Payload>(encoded, out var payload));
        Assert.True(payload?.Ok);
        Assert.False(codec.TryDecode<Payload>(
            "{\"schema_version\":2,\"created_at_utc\":\"2026-07-16T00:00:00Z\",\"payload\":{\"ok\":true}}"u8,
            out _));
        Assert.False(codec.TryDecode<Payload>("{\"schema_version\":1,\"payload\":{\"ok\":true}}"u8, out _));
        Assert.False(codec.TryDecode<Payload>("not-json"u8, out _));
    }

    [Fact]
    public void Ttl_jitter_is_deterministic_and_has_one_second_minimum()
    {
        var low = new CacheTtlJitter(new FakeRandom(0), new CachingOptions { TtlJitterPercent = 10 });
        Assert.Equal(TimeSpan.FromSeconds(9), low.Apply(TimeSpan.FromSeconds(10)));
        var minimum = new CacheTtlJitter(new FakeRandom(0), new CachingOptions { TtlJitterPercent = 100 });
        Assert.Equal(TimeSpan.FromSeconds(1), minimum.Apply(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Circuit_opens_after_five_failures_and_recovers_after_half_open_success()
    {
        var clock = new FakeClock();
        var circuit = new CacheCircuitBreaker(clock);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.True(circuit.AllowRequest());
            circuit.RecordFailure();
        }
        Assert.Equal("open", circuit.State);
        Assert.False(circuit.AllowRequest());
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.True(circuit.AllowRequest());
        Assert.False(circuit.AllowRequest());
        circuit.RecordSuccess();
        Assert.Equal("closed", circuit.State);
        Assert.True(circuit.AllowRequest());
    }

    private sealed record Payload(bool Ok);

    private sealed class FakeRandom(double value) : ICacheRandom
    {
        public double NextDouble() => value;
    }

    private sealed class FakeClock : ICacheClock
    {
        private long _timestamp;
        public DateTimeOffset UtcNow => new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
        public long GetTimestamp() => _timestamp;
        public TimeSpan GetElapsedTime(long startingTimestamp) =>
            TimeSpan.FromTicks(_timestamp - startingTimestamp);
        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}
