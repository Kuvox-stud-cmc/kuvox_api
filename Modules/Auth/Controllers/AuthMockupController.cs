using Kuvox.Api.Modules.Auth.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Kuvox.Api.Modules.Auth.Controllers;

/// <summary>
/// Mockup Auth endpoints returning canned data (no DB, no auth). Lets the frontend team
/// integrate against a stable contract before the real implementation lands.
/// </summary>
[ApiController]
[Route("api/mock/auth")]
[Produces("application/json")]
public sealed class AuthMockupController : ControllerBase
{
    private static readonly UserDto SampleUser = new(
        Guid.Parse("0192a3b4-c5d6-7e8f-9a0b-1c2d3e4f5061"),
        "creator@example.com",
        "Sample Creator",
        "user",
        "Free",
        EmailVerified: true,
        DateTimeOffset.UtcNow.AddDays(-7));

    [HttpPost("register")]
    public ActionResult<UserDto> Register(RegisterRequest request) =>
        Ok(SampleUser with { Email = request.Email, DisplayName = request.DisplayName });

    [HttpPost("login")]
    public ActionResult<AuthTokenDto> Login(LoginRequest request) =>
        Ok(new AuthTokenDto(
            AccessToken: "mock.access.token",
            RefreshToken: "mock.refresh.token",
            ExpiresAt: DateTimeOffset.UtcNow.AddHours(1)));

    [HttpGet("me")]
    public ActionResult<UserDto> Me() => Ok(SampleUser);
}
