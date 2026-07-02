using Kuvox.Api.Modules.Shared.Models;

namespace Kuvox.Api.Modules.Projects.Models;

public abstract class ProjectMedia : JunctionBaseEntity
{
  public required Guid ProjectId { get; set; }
  public required Guid MediaId { get; set; }
}
