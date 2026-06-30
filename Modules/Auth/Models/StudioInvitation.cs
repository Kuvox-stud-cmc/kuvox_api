using Kuvox.Api.Modules.Auth.Enums;
using Kuvox.Api.Modules.Shared.Models;

namespace Kuvox.Api.Modules.Auth.Models;

public sealed class StudioInvitation : BaseEntity
{
    public required Guid StudioId { get; set; }

    public required string Email { get; set; }

    public required UserStudioRole Role { get; set; }

    public required Guid InvitedByUserId { get; set; }

    public required string TokenHash { get; set; }

    public StudioInvitationStatus Status { get; set; } = StudioInvitationStatus.Pending;

    public required DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }

    public DateTimeOffset? DeclinedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}
