using Kuvox.Api.Modules.Shared.Models;

namespace Kuvox.Api.Modules.Auth.Models;

public sealed class Studio : BaseEntity
{
    public required string Name { get; set;}

    public string? Description { get; set; }

    public string? AvatarUrl { get; set; }

    public string? PublicSlug { get; set; }

    public int InvitationExpiryDays { get; set; } = 7;

    public bool NotifyOnInvites { get; set; } = true;

    public bool NotifyOnMembers { get; set; } = true;

    public bool NotifyOnProjects { get; set; } = true;

    public bool NotifyOnMedia { get; set; } = true;
}
