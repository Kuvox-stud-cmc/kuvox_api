using Microsoft.AspNetCore.Mvc;

namespace Kuvox.Api.Modules.Shared.Controllers;

[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    /// <summary>Liveness probe — does not touch the database.</summary>
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "ok", service = "kuvox-api" });
}
