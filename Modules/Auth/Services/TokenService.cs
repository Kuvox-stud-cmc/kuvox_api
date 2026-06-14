using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Kuvox.Api.Modules.Auth.Enums;
using Kuvox.Api.Modules.Auth.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Kuvox.Api.Modules.Auth.Services;

/// <summary>
/// Default <see cref="ITokenService"/>: HMAC-SHA256 signed access JWTs and opaque
/// (random 256-bit) refresh tokens. Internal (Rule 1).
/// </summary>
internal sealed class TokenService(IOptions<JwtOptions> options) : ITokenService
{
    /// <summary>Custom claim type carrying the user's plan (Free/Creator) for authorization.</summary>
    public const string PlanClaimType = "plan";

    /// <summary>Custom claim type carrying a studio membership encoded as <c>{studioId}:{role}</c>.</summary>
    public const string StudioClaimType = "studio";

    private readonly JwtOptions _options = options.Value;

    public (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(
        User user, IReadOnlyList<(Guid StudioId, UserStudioRole Role)> memberships)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Name, user.DisplayName),
            new(PlanClaimType, user.Plan.ToString()),
        };

        claims.AddRange(memberships.Select(m =>
            new Claim(StudioClaimType, $"{m.StudioId}:{m.Role}")));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public (string Token, string TokenHash, DateTimeOffset ExpiresAt) CreateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(bytes);
        var expiresAt = DateTimeOffset.UtcNow.AddDays(_options.RefreshTokenDays);
        return (token, HashToken(token), expiresAt);
    }

    public (string Token, string TokenHash, DateTimeOffset ExpiresAt) CreateSingleUseToken(TimeSpan lifetime)
    {
        // URL-safe opaque token so it can ride in an email link query string unescaped.
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var expiresAt = DateTimeOffset.UtcNow.Add(lifetime);
        return (token, HashToken(token), expiresAt);
    }

    public string HashToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash);
    }
}
