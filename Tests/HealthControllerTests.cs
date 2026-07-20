using System.Text.Json;
using Kuvox.Api.Modules.Shared.Controllers;
using Kuvox.Api.Modules.Shared.Infrastructure.Caching;
using Kuvox.Api.Modules.Shared.Infrastructure.Health;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;

namespace Tests;

public sealed class HealthControllerTests
{
    [Theory]
    [InlineData(true, false, false, 200, "healthy", "disabled")]
    [InlineData(true, true, false, 200, "degraded", "unhealthy")]
    [InlineData(false, true, true, 503, "unhealthy", "healthy")]
    public async Task Ready_applies_required_postgres_and_optional_redis_contract(
        bool postgresHealthy,
        bool cacheEnabled,
        bool redisHealthy,
        int expectedStatus,
        string expectedOverall,
        string expectedRedis)
    {
        var controller = new HealthController(
            new FakePostgres(postgresHealthy),
            new FakeRedis(redisHealthy),
            Options.Create(new CachingOptions { Enabled = cacheEnabled }));
        var result = await controller.Ready(CancellationToken.None);
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(expectedStatus, objectResult.StatusCode ?? StatusCodes.Status200OK);
        var json = JsonSerializer.SerializeToElement(objectResult.Value);
        Assert.Equal(expectedOverall, json.GetProperty("status").GetString());
        var redis = json.GetProperty("dependencies").EnumerateArray().Single(
            item => item.GetProperty("name").GetString() == "redis");
        Assert.Equal(expectedRedis, redis.GetProperty("status").GetString());
        Assert.False(redis.GetProperty("required").GetBoolean());
    }

    private sealed class FakePostgres(bool healthy) : IPostgresReadinessProbe
    {
        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) => Task.FromResult(healthy);
    }

    private sealed class FakeRedis(bool healthy) : IRedisConnectionProvider
    {
        public Task<IDatabase> GetDatabaseAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) => Task.FromResult(healthy);
    }
}
