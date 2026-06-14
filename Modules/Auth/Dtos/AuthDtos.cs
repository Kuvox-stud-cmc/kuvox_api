namespace Kuvox.Api.Modules.Auth.Dtos;

/// <summary>Module-private request/response shapes for the Auth HTTP surface.</summary>
public sealed record RegisterRequest(string Email, string Password, string DisplayName);

public sealed record LoginRequest(string Email, string Password);

public sealed record UserDto(Guid Id, string Email, string DisplayName, string Role, string Plan, bool EmailVerified, DateTimeOffset CreatedAt);

/// <summary>Issued JWT pair returned on login/refresh.</summary>
public sealed record AuthTokenDto(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

public sealed record VerifyEmailRequest(string Token);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Token, string NewPassword);
