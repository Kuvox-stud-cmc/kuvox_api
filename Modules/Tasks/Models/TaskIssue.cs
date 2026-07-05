using Kuvox.Api.Modules.Shared.Models;
using Kuvox.Api.Modules.Tasks.Contracts;

namespace Kuvox.Api.Modules.Tasks.Models;

internal sealed class TaskIssue : BaseEntity
{
    public required Guid StudioId { get; set; }

    public Guid? ProjectId { get; set; }

    public Guid? ParentTaskIssueId { get; set; }

    public required TaskIssueKind Kind { get; set; }

    public TaskIssueStatus Status { get; set; } = TaskIssueStatus.Open;

    public required string Title { get; set; }

    public string? Description { get; set; }

    public DateTimeOffset? DueDate { get; set; }

    public Guid? MilestoneId { get; set; }

    public required Guid CreatedByUserId { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public TaskMilestone? Milestone { get; set; }

    public TaskIssue? ParentTaskIssue { get; set; }

    public ICollection<TaskIssue> Children { get; } = new List<TaskIssue>();

    public ICollection<TaskAssignee> Assignees { get; } = new List<TaskAssignee>();

    public ICollection<TaskIssueLabel> Labels { get; } = new List<TaskIssueLabel>();

    public ICollection<TaskComment> Comments { get; } = new List<TaskComment>();

    public ICollection<TaskActivity> Activities { get; } = new List<TaskActivity>();
}
