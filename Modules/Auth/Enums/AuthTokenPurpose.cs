namespace Kuvox.Api.Modules.Auth.Enums;

/// <summary>Discriminates the kind of single-use token stored in <c>auth.auth_tokens</c>.</summary>
public enum AuthTokenPurpose
{
    EmailVerification,
    PasswordReset,
}
