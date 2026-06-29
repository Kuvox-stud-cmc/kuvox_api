using Kuvox.Api.Modules.Projects.Enums;
using Kuvox.Api.Modules.Shared.Models;

namespace Kuvox.Api.Modules.Projects.Models;

public sealed class ProjectActivity : ImmutableBaseEntity
{
    public required Guid ProjectId { get; set; }

    public required Guid UserId { get; set; }

    ///<summary>Optional: If the action happened inside the editor on a specific timeline</summary>
    public Guid? TimelineId { get; set; }

    ///<summary>Optional: If the action happened on a specific media item</summary>
    public Guid? MediaId { get; set; }

    public required ActivityAction Action { get; set; }

    public string? MetadataJson { get; set; }
}