using Kuvox.Api.Modules.Auth.Enums;
using Kuvox.Api.Modules.Auth.Models;

namespace Kuvox.Api.Modules.Auth.Services;

/// <summary>
/// Issues access JWTs and opaque refresh tokens. Centralizes the signing key, claim
/// shape, and lifetimes so <see cref="AuthService"/> stays free of crypto details.
/// Internal to the Auth module.
/// </summary>
internal interface ITokenService
{
    /// <summary>Builds a signed access JWT with sub/email/name/plan + one studio claim per membership.</summary>
    (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(
        User user, IReadOnlyList<(Guid StudioId, UserStudioRole Role)> memberships);

    /// <summary>Generates a cryptographically random opaque refresh token + its SHA-256 hash.</summary>
    (string Token, string TokenHash, DateTimeOffset ExpiresAt) CreateRefreshToken();

    /// <summary>
    /// Generates a cryptographically random single-use token (email verification / password
    /// reset) + its SHA-256 hash, expiring after <paramref name="lifetime"/>.
    /// </summary>
    (string Token, string TokenHash, DateTimeOffset ExpiresAt) CreateSingleUseToken(TimeSpan lifetime);

    /// <summary>SHA-256 hex hash of an opaque token (for refresh-token lookups).</summary>
    string HashToken(string token);
}
