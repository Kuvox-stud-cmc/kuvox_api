using Kuvox.Api.Modules.Shared.Infrastructure.Caching;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tests;

public sealed class CachePrewarmTests
{
    [Fact]
    public void Bounded_queue_drops_overflow_without_throwing()
    {
        var options = new CachingOptions
        {
            Enabled = true,
            BusinessReads = new CacheFeatureOptions { Enabled = true },
            Studio = new StudioCacheOptions { Enabled = true },
            StudioSettingsPrewarmEnabled = true,
            PrewarmQueueCapacity = 1,
        };
        var queue = new CachePrewarmQueue(Options.Create(options));

        Assert.True(queue.TryQueue(new CachePrewarmRequest(
            CachePrewarmKind.StudioSettings, Guid.NewGuid(), "generation-one")));
        Assert.False(queue.TryQueue(new CachePrewarmRequest(
            CachePrewarmKind.StudioSettings, Guid.NewGuid(), "generation-two")));
    }

    [Fact]
    public void Disabled_target_never_enters_queue()
    {
        var queue = new CachePrewarmQueue(Options.Create(new CachingOptions
        {
            Enabled = true,
            BusinessReads = new CacheFeatureOptions { Enabled = true },
            Studio = new StudioCacheOptions { Enabled = true },
            StudioSettingsPrewarmEnabled = false,
        }));

        Assert.False(queue.TryQueue(new CachePrewarmRequest(
            CachePrewarmKind.StudioSettings, Guid.NewGuid(), "generation")));
    }
}
