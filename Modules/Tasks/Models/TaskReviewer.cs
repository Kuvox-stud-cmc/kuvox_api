using Kuvox.Api.Modules.Shared.Models;

namespace Kuvox.Api.Modules.Tasks.Models;

internal sealed class TaskReviewer : JunctionBaseEntity
{
    public required Guid TaskIssueId { get; set; }

    public required Guid UserId { get; set; }

    public TaskIssue? TaskIssue { get; set; }
}
