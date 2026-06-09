using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Kuvox.Api.Modules.Shared.Infrastructure;

/// <summary>
/// Reads the Kuvox JWT claim shape off a <see cref="ClaimsPrincipal"/>. These helpers live in
/// Shared (not Auth) so non-Auth modules can resolve the caller and their workspaces without
/// depending on Auth internals (Rule 1). The claim-type strings mirror the wire contract
/// produced by Auth's <c>TokenService</c>.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>Studio membership claim, encoded as <c>{studioId}:{role}</c> (mirrors TokenService).</summary>
    public const string StudioClaimType = "studio";

    /// <summary>Resolves the caller's user id from the <c>sub</c> claim (or its mapped form).</summary>
    public static Guid? GetUserId(this ClaimsPrincipal user)
    {
        // With default inbound-claim mapping, "sub" is surfaced as ClaimTypes.NameIdentifier.
        var sub = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    /// <summary>Parses every <c>studio</c> claim into (studioId, role) pairs.</summary>
    public static IReadOnlyList<(Guid StudioId, string Role)> GetStudios(this ClaimsPrincipal user)
    {
        var studios = new List<(Guid, string)>();
        foreach (var claim in user.FindAll(StudioClaimType))
        {
            var sep = claim.Value.IndexOf(':');
            if (sep <= 0)
            {
                continue;
            }

            if (Guid.TryParse(claim.Value[..sep], out var studioId))
            {
                studios.Add((studioId, claim.Value[(sep + 1)..]));
            }
        }

        return studios;
    }

    /// <summary>True if the caller carries a membership claim for the given studio.</summary>
    public static bool BelongsToStudio(this ClaimsPrincipal user, Guid studioId) =>
        user.GetStudios().Any(s => s.StudioId == studioId);

    /// <summary>True if the caller carries an <c>Admin</c> membership claim for the given studio.</summary>
    public static bool IsStudioAdmin(this ClaimsPrincipal user, Guid studioId) =>
        user.GetStudios().Any(s => s.StudioId == studioId && string.Equals(s.Role, "Admin", StringComparison.Ordinal));

    /// <summary>Snapshots the caller (id + studios) for passing into services.</summary>
    public static CallerContext? ToCallerContext(this ClaimsPrincipal user)
    {
        var userId = user.GetUserId();
        return userId is null ? null : new CallerContext(userId.Value, user.GetStudios());
    }
}
