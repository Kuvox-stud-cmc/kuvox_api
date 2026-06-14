using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Kuvox.Api.Modules.Auth.Dtos;
using Kuvox.Api.Modules.Auth.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kuvox.Api.Modules.Auth.Controllers;

/// <summary>
/// Real Auth endpoints backed by <see cref="IAuthService"/>: register, login, refresh,
/// logout, and the authenticated <c>/me</c> projection.
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(IAuthService auth) : ControllerBase
{
    [HttpPost("register")]
    public Task<UserDto> Register(RegisterRequest request, CancellationToken ct) =>
        auth.RegisterAsync(request, ct);

    [HttpPost("login")]
    public Task<AuthTokenDto> Login(LoginRequest request, CancellationToken ct) =>
        auth.LoginAsync(request, ct);

    [HttpPost("refresh")]
    public Task<AuthTokenDto> Refresh([FromBody] string refreshToken, CancellationToken ct) =>
        auth.RefreshAsync(refreshToken, ct);

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] string refreshToken, CancellationToken ct)
    {
        await auth.LogoutAsync(refreshToken, ct);
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct)
    {
        var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(sub, out var userId))
        {
            return Unauthorized();
        }

        var user = await auth.GetCurrentUserAsync(userId, ct);
        return user is null ? NotFound() : Ok(user);
    }

    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail(VerifyEmailRequest request, CancellationToken ct)
    {
        await auth.VerifyEmailAsync(request.Token, ct);
        return NoContent();
    }

    [Authorize]
    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification(CancellationToken ct)
    {
        var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(sub, out var userId))
        {
            return Unauthorized();
        }

        await auth.ResendVerificationAsync(userId, ct);
        return NoContent();
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        await auth.ForgotPasswordAsync(request.Email, ct);
        return NoContent();
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        await auth.ResetPasswordAsync(request.Token, request.NewPassword, ct);
        return NoContent();
    }
}

