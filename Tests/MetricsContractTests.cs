using System.Text;
using Kuvox.Api.Modules.Shared.Infrastructure.Caching;
using Prometheus;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Tests;

public sealed class MetricsContractTests
{
    [Fact]
    public async Task Cache_metrics_expose_expected_series_without_sensitive_labels()
    {
        await new DisabledCacheStore().GetAsync("ignored");
        var options = new CachingOptions();
        var business = new BusinessCache(
            new DisabledCacheStore(),
            new JsonCacheCodec(new SystemCacheClock()),
            Options.Create(options),
            NullLogger<BusinessCache>.Instance);
        await business.GetOrCreateAsync(
            "projects", "detail", options.Projects, "ignored", TimeSpan.FromSeconds(30),
            _ => Task.FromResult(new { ok = true }));
        await using var stream = new MemoryStream();
        await Metrics.DefaultRegistry.CollectAndExportAsTextAsync(stream);
        var exposition = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains("kuvox_cache_operations_total", exposition, StringComparison.Ordinal);
        Assert.Contains("kuvox_cache_circuit_state", exposition, StringComparison.Ordinal);
        Assert.Contains("kuvox_business_cache_operations_total", exposition, StringComparison.Ordinal);
        Assert.Contains("kuvox_business_cache_duration_seconds", exposition, StringComparison.Ordinal);
        Assert.Contains("kuvox_business_cache_payload_bytes", exposition, StringComparison.Ordinal);
        Assert.Contains("kuvox_business_cache_invalidations_total", exposition, StringComparison.Ordinal);
        Assert.Contains("kuvox_business_cache_generation_operations_total", exposition, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "user_id", "studio_id", "project_id", "media_id", "query", "token" })
        {
            Assert.DoesNotContain($"{forbidden}=\"", exposition, StringComparison.Ordinal);
        }
    }
}
