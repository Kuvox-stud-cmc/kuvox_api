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

    public string? StudioRole(Guid studioId) =>
        Studios.FirstOrDefault(s => s.StudioId == studioId).Role;

    public bool IsStudioOwner(Guid studioId) =>
        Studios.Any(s => s.StudioId == studioId && string.Equals(s.Role, "Owner", StringComparison.Ordinal));

    /// <summary>True if the caller is an <c>Owner</c> or <c>Admin</c> of the given studio.</summary>
    public bool IsStudioAdmin(Guid studioId) =>
        Studios.Any(s => s.StudioId == studioId && IsPrivilegedRole(s.Role));

    public bool CanWriteStudioContent(Guid studioId) =>
        Studios.Any(s => s.StudioId == studioId && IsContentWriterRole(s.Role));

    public bool CanManageStudioAccess(Guid studioId) => IsStudioAdmin(studioId);

    private static bool IsPrivilegedRole(string role) =>
        string.Equals(role, "Owner", StringComparison.Ordinal)
        || string.Equals(role, "Admin", StringComparison.Ordinal);

    private static bool IsContentWriterRole(string role) =>
        IsPrivilegedRole(role)
        || string.Equals(role, "Member", StringComparison.Ordinal)
        || string.Equals(role, "User", StringComparison.Ordinal);
}
