using Kuvox.Api.Modules.Shared.Models;

namespace Kuvox.Api.Modules.Media.Models;

public sealed class Album : BaseEntity
{
  public required string Name { get; set; }
  
  public required string Description { get; set; }
}
