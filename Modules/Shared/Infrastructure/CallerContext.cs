namespace Kuvox.Api.Modules.Shared.Infrastructure;

/// <summary>
/// The authenticated caller as seen from the JWT: their user id plus the studios they belong
/// to (with role). Built once per request from the <c>ClaimsPrincipal</c>
/// (see <see cref="ClaimsPrincipalExtensions.ToCallerContext"/>) and passed into services so
/// authorization decisions on a loaded resource stay explicit and testable.
/// </summary>
public sealed record CallerContext(Guid UserId, IReadOnlyList<(Guid StudioId, string Role)> Studios)
{
    /// <summary>True if the caller is the user that personally owns <paramref name="ownerId"/>.</summary>
    public bool OwnsAsUser(Guid ownerId) => ownerId == UserId;

    /// <summary>True if the caller is a member of the given studio (any role).</summary>
    public bool InStudio(Guid studioId) => Studios.Any(s => s.StudioId == studioId);

    /// <summary>True if the caller is an <c>Admin</c> of the given studio.</summary>
    public bool IsStudioAdmin(Guid studioId) =>
        Studios.Any(s => s.StudioId == studioId && string.Equals(s.Role, "Admin", StringComparison.Ordinal));
}
