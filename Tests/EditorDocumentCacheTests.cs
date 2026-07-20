using Kuvox.Api.Modules.Shared.Infrastructure.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tests;

public sealed class EditorDocumentCacheTests
{
    private static readonly Guid ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid JobId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Keys_are_exact_revision_addressed_and_domain_isolated()
    {
        var (cache, _) = Create();
        var updatedAt = new DateTimeOffset(2026, 7, 19, 1, 2, 3, TimeSpan.Zero);

        Assert.Equal(
            "kuvox:v1:api:timeline-document:project:11111111111111111111111111111111:revision:7:schema:1",
            cache.TimelineDocumentKey(ProjectId, 7));
        Assert.Equal(
            "kuvox:v1:api:timeline-list:project:11111111111111111111111111111111:revision:7:schema:1",
            cache.TimelineListKey(ProjectId, 7));
        Assert.Equal(
            "kuvox:v1:api:image-composition:project:11111111111111111111111111111111:revision:7:schema:1",
            cache.ImageCompositionKey(ProjectId, 7));
        Assert.Equal(
            $"kuvox:v1:api:render-job:job:22222222222222222222222222222222:status:queued:updated:{updatedAt.UtcTicks}:schema:1",
            cache.RenderJobKey(JobId, "queued", updatedAt));
    }

    [Fact]
    public async Task Document_and_render_writes_use_independent_ttls()
    {
        var (cache, store) = Create();

        await cache.WriteTimelineDocumentAsync(ProjectId, 1, new Payload(1));
        Assert.Equal(TimeSpan.FromSeconds(15), store.LastTtl);

        await cache.WriteRenderJobAsync(JobId, "queued", DateTimeOffset.UtcNow, new Payload(2));
        Assert.Equal(TimeSpan.FromSeconds(3), store.LastTtl);
    }

    [Fact]
    public async Task Serialization_and_cache_cancellation_fail_open()
    {
        var (cache, store) = Create();
        var cyclic = new Cyclic();
        cyclic.Self = cyclic;

        await cache.WriteTimelineDocumentAsync(ProjectId, 1, cyclic);
        Assert.Empty(store.Values);

        store.ThrowOnRead = new OperationCanceledException("cache only");
        var value = await cache.GetTimelineDocumentAsync(
            ProjectId,
            2,
            _ => Task.FromResult(new Payload(9)),
            CancellationToken.None);
        Assert.Equal(9, value.Value);
    }

    [Fact]
    public async Task Oversized_values_are_returned_but_not_cached()
    {
        var (cache, store) = Create(maxPayloadBytes: 128);
        var value = await cache.GetTimelineDocumentAsync(
            ProjectId,
            3,
            _ => Task.FromResult(new string('x', 512)),
            CancellationToken.None);

        Assert.Equal(512, value.Length);
        Assert.Empty(store.Values);
    }

    private static (EditorDocumentCache Cache, MemoryStore Store) Create(int maxPayloadBytes = 1_048_576)
    {
        var options = new CachingOptions
        {
            Enabled = true,
            BusinessReads = new CacheFeatureOptions { Enabled = true },
            EditorDocuments = new CacheFeatureOptions { Enabled = true, TtlSeconds = 15 },
            RenderJobs = new CacheFeatureOptions { Enabled = true, TtlSeconds = 3 },
            MaxPayloadBytes = maxPayloadBytes,
        };
        var store = new MemoryStore();
        var business = new BusinessCache(
            store,
            new JsonCacheCodec(new SystemCacheClock()),
            Options.Create(options),
            NullLogger<BusinessCache>.Instance);
        return (new EditorDocumentCache(business, new CacheKeyFactory(options), Options.Create(options)), store);
    }

    private sealed record Payload(int Value);

    private sealed class Cyclic
    {
        public Cyclic? Self { get; set; }
    }

    private sealed class MemoryStore : ICacheStore
    {
        public Dictionary<string, byte[]> Values { get; } = [];
        public Exception? ThrowOnRead { get; set; }
        public TimeSpan LastTtl { get; private set; }

        public Task<CacheReadResult> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            if (ThrowOnRead is { } error) throw error;
            return Task.FromResult(Values.TryGetValue(key, out var value)
                ? new CacheReadResult(CacheReadOutcome.Hit, value)
                : new CacheReadResult(CacheReadOutcome.Miss));
        }

        public Task<CacheWriteOutcome> SetAsync(string key, ReadOnlyMemory<byte> value, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            LastTtl = ttl;
            Values[key] = value.ToArray();
            return Task.FromResult(CacheWriteOutcome.Success);
        }

        public Task<CacheWriteOutcome> DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            Values.Remove(key);
            return Task.FromResult(CacheWriteOutcome.Success);
        }
    }
}
