using Kuvox.Api.Modules.Shared.Models;

namespace Kuvox.Api.Modules.Media.Models;

public abstract class AlbumMedia : JunctionBaseEntity
{
  public required Guid AlbumId { get; set; }
  public required Guid MediaId { get; set; }
}
