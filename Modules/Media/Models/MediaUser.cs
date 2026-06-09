using Kuvox.Api.Modules.Media.Enums;
using Kuvox.Api.Modules.Shared.Models;

namespace Kuvox.Api.Modules.Media.Models;

/// <summary>
/// Grants a non-owner user access to a media item (composite <c>media.media_users</c>).
/// Owner access stays denormalized on <see cref="Media.OwnerId"/>; this table is the
/// "shared with me" set (Rule 1 — references users by id only, no cross-schema FK).
/// </summary>
public sealed class MediaUser : JunctionBaseEntity
{
    public required Guid MediaId { get; set; }

    public required Guid UserId { get; set; }

    public required MediaRole Role { get; set; }
}
