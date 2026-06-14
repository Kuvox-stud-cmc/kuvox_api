using Kuvox.Api.Modules.Auth.Dtos;

namespace Kuvox.Api.Modules.Auth.Services;

/// <summary>
/// Module-internal business API of the Auth module — the "real" surface the
/// <c>AuthController</c> delegates to. Public only because the public controller injects it;
/// the implementation stays <c>internal</c> (Rule 1) and other modules must not consume it —
/// the cross-module surface is <c>Auth.Contracts</c> (Rule 2).
/// </summary>
public interface IAuthService
{
    Task<UserDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    Task<AuthTokenDto> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task<AuthTokenDto> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

    Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default);

    Task<UserDto?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<VerifyEmailResult> VerifyEmailAsync(string token, CancellationToken cancellationToken = default);

    Task ResendVerificationAsync(Guid userId, CancellationToken cancellationToken = default);

    Task ResendVerificationByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task ForgotPasswordAsync(string email, CancellationToken cancellationToken = default);

    Task ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken = default);
}
