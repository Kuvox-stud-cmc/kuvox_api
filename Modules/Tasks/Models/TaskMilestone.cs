using Kuvox.Api.Modules.Shared.Models;
using Kuvox.Api.Modules.Tasks.Contracts;

namespace Kuvox.Api.Modules.Tasks.Models;

internal sealed class TaskMilestone : BaseEntity
{
    public required Guid StudioId { get; set; }

    public required string Title { get; set; }

    public string? Description { get; set; }

    public DateTimeOffset? DueDate { get; set; }

    public TaskMilestoneStatus Status { get; set; } = TaskMilestoneStatus.Open;
}
