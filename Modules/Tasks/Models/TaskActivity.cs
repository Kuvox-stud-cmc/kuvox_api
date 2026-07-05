using Kuvox.Api.Modules.Shared.Models;

namespace Kuvox.Api.Modules.Tasks.Models;

internal sealed class TaskActivity : BaseEntity
{
    public required Guid StudioId { get; set; }

    public required Guid TaskIssueId { get; set; }

    public Guid? ActorUserId { get; set; }

    public required string Action { get; set; }

    public required string Summary { get; set; }

    public string? MetadataJson { get; set; }

    public TaskIssue? TaskIssue { get; set; }
}
