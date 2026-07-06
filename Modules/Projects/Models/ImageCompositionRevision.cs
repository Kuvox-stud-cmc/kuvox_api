using Kuvox.Api.Modules.Shared.Models;

namespace Kuvox.Api.Modules.Projects.Models;

public sealed class ImageCompositionRevision : ImmutableBaseEntity
{
    public required Guid ImageCompositionId { get; set; }

    public required Guid ProjectId { get; set; }

    public int RevisionNumber { get; set; }

    public required string DocumentJson { get; set; }

    public required string OperationsJson { get; set; }

    public required Guid CreatedByUserId { get; set; }
}
