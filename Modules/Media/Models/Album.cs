using Kuvox.Api.Modules.Media.Enums;
using Kuvox.Api.Modules.Shared.Models;

namespace Kuvox.Api.Modules.Media.Models;

public sealed class Album : BaseEntity
{
  public required string Name { get; set; }
  
  public required string Description { get; set; }

  public required AlbumKind Kind { get; set; }

  public required string MaterialSymbol { get; set; } = "folder";

  public required bool IsDeleteAble { get; set; } = true;
}
