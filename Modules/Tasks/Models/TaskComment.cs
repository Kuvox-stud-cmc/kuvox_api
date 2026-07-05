using Kuvox.Api.Modules.Shared.Models;

namespace Kuvox.Api.Modules.Tasks.Models;

internal sealed class TaskComment : BaseEntity
{
    public required Guid StudioId { get; set; }

    public required Guid TaskIssueId { get; set; }

    public required Guid AuthorUserId { get; set; }

    public required string Body { get; set; }

    public DateTimeOffset? EditedAt { get; set; }

    public TaskIssue? TaskIssue { get; set; }
}
