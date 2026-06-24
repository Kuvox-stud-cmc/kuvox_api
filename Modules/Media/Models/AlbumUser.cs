using Kuvox.Api.Modules.Shared.Models;
using Kuvox.Api.Modules.Media.Enums;

namespace Kuvox.Api.Modules.Media.Models;

public sealed class AlbumUser : JunctionBaseEntity
{
  public required Guid AlbumId { get; set; }

  public required Guid UserId { get; set; }

  public required Permission Role { get; set; } = Permission.Owner;
}
