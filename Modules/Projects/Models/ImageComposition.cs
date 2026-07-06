using Kuvox.Api.Modules.Shared.Models;

namespace Kuvox.Api.Modules.Projects.Models;

public sealed class ImageComposition : BaseEntity
{
    public required Guid ProjectId { get; set; }

    public required string DocumentJson { get; set; }

    public int RevisionNumber { get; set; }

    public required Guid UpdatedByUserId { get; set; }
}
