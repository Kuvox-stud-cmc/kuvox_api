using Kuvox.Api.Modules.Auth.Dtos;
using Kuvox.Api.Modules.Auth.Services;
using Microsoft.AspNetCore.Mvc;

namespace Kuvox.Api.Modules.Auth.Controllers;

/// <summary>
/// Real Auth endpoints. The route surface is final, but the backing
/// <see cref="IAuthService"/> is not implemented yet — calls return <c>501 Not Implemented</c>.
/// Use <c>/api/mock/auth</c> for working fake data during development.
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
}
