using Kuvox.Api.Modules.Shared.Models;
using Kuvox.Api.Modules.Auth.Enums;
namespace Kuvox.Api.Modules.Auth.Models;

/// <summary>A registered Kuvox user. Owned by the Auth module (table <c>auth.users</c>).</summary>
public sealed class User : BaseEntity
{
    public required string Email { get; set; }

    public required string PasswordHash { get; set; }

    public required string DisplayName { get; set; }

    public UserPlan Plan { get; set; } = UserPlan.Free;

    /// <summary>When the user confirmed their email; <c>null</c> while unverified (soft gate).</summary>
    public DateTimeOffset? EmailVerifiedAt { get; set; }
}
