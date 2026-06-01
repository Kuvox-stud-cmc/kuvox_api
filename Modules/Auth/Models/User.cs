using Kuvox.Api.Modules.Shared.Models;

namespace Kuvox.Api.Modules.Auth.Models;

/// <summary>A registered Kuvox user. Owned by the Auth module (table <c>auth.users</c>).</summary>
public sealed class User : BaseEntity
{
    public required string Email { get; set; }

    public required string PasswordHash { get; set; }

    public required string DisplayName { get; set; }

    /// <summary>Coarse role claim, e.g. "user" or "admin".</summary>
    public string Role { get; set; } = "user";
}
