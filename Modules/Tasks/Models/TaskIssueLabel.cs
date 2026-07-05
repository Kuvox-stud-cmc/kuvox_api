using Kuvox.Api.Modules.Shared.Models;

namespace Kuvox.Api.Modules.Tasks.Models;

internal sealed class TaskIssueLabel : JunctionBaseEntity
{
    public required Guid TaskIssueId { get; set; }

    public required Guid TaskLabelId { get; set; }

    public TaskIssue? TaskIssue { get; set; }

    public TaskLabel? TaskLabel { get; set; }
}
