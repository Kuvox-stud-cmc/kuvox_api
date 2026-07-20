using Kuvox.Api.Modules.Shared.Infrastructure.Caching;
using Kuvox.Api.Modules.Shared.Infrastructure.Health;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Kuvox.Api.Modules.Shared.Controllers;

[ApiController]
[Route("health")]
public sealed class HealthController(
    IPostgresReadinessProbe postgres,
    IRedisConnectionProvider redis,
    IOptions<CachingOptions> caching) : ControllerBase
{
    /// <summary>Liveness probe — does not touch the database.</summary>
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "ok", service = "kuvox-api" });

    /// <summary>Process-only liveness alias.</summary>
    [HttpGet("live")]
    public IActionResult Live() => Get();

    /// <summary>Readiness checks required PostgreSQL and optional Redis.</summary>
    [HttpGet("ready")]
    public async Task<IActionResult> Ready(CancellationToken cancellationToken)
    {
        var postgresHealthy = await postgres.IsHealthyAsync(cancellationToken);
        var redisStatus = "disabled";
        if (caching.Value.Enabled)
        {
            redisStatus = await redis.IsHealthyAsync(cancellationToken) ? "healthy" : "unhealthy";
        }

        var overall = !postgresHealthy
            ? "unhealthy"
            : redisStatus == "unhealthy" ? "degraded" : "healthy";
        var body = new
        {
            status = overall,
            dependencies = new object[]
            {
                new { name = "postgres", status = postgresHealthy ? "healthy" : "unhealthy", required = true },
                new { name = "redis", status = redisStatus, required = false }
            }
        };

        return postgresHealthy ? Ok(body) : StatusCode(StatusCodes.Status503ServiceUnavailable, body);
    }
}
