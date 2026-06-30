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
        var user = await auth.GetCurrentUserAsync(CurrentUserId(), ct);
        return user is null ? NotFound() : Ok(user);
    }

    [Authorize]
    [HttpGet("me/settings")]
    public async Task<ActionResult<UserSettingsDto>> Settings(CancellationToken ct)
    {
        var settings = await auth.GetSettingsAsync(CurrentUserId(), ct);
        return settings is null ? NotFound() : Ok(settings);
    }

    [Authorize]
    [HttpPatch("me/profile")]
    public async Task<ActionResult<UserDto>> UpdateProfile(UpdateProfileRequest request, CancellationToken ct) =>
        Ok(await auth.UpdateProfileAsync(CurrentUserId(), request, ct));

    [Authorize]
    [HttpPatch("me/preferences")]
    public async Task<ActionResult<UserPreferencesDto>> UpdatePreferences(UpdatePreferencesRequest request, CancellationToken ct) =>
        Ok(await auth.UpdatePreferencesAsync(CurrentUserId(), request, ct));

    [Authorize]
    [HttpPost("me/change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        await auth.ChangePasswordAsync(CurrentUserId(), request, ct);
        return NoContent();
    }

    [Authorize]
    [HttpPost("me/resend-verification")]
    public async Task<IActionResult> ResendVerification(CancellationToken ct)
    {
        await auth.ResendVerificationAsync(CurrentUserId(), ct);
        return NoContent();
    }

    [HttpPost("verify-email")]
    public async Task<ActionResult<VerifyEmailResult>> VerifyEmail(VerifyEmailRequest request, CancellationToken ct) =>
        Ok(await auth.VerifyEmailAsync(request.Token, ct));

    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification(ResendVerificationRequest request, CancellationToken ct)
    {
        // Anonymous: unverified users have no session. Responds neutrally (no user enumeration).
        await auth.ResendVerificationByEmailAsync(request.Email, ct);
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

    private Guid CurrentUserId()
    {
        var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(sub, out var userId))
        {
            throw AuthException.Unauthorized("Invalid token.");
        }

        return userId;
    }
}
