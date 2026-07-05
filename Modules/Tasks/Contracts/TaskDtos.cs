namespace Kuvox.Api.Modules.Tasks.Contracts;

public sealed record TaskIssueDto(
    Guid Id,
    Guid StudioId,
    Guid? ProjectId,
    string? ProjectName,
    Guid? ParentTaskIssueId,
    TaskIssueKind Kind,
    TaskIssueStatus Status,
    string Title,
    string? Description,
    DateTimeOffset? DueDate,
    TaskMilestoneDto? Milestone,
    IReadOnlyList<TaskAssigneeDto> Assignees,
    IReadOnlyList<TaskReviewerDto> Reviewers,
    IReadOnlyList<TaskLabelDto> Labels,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ClosedAt,
    int CommentsCount,
    int SubtaskCount,
    int CompletedSubtaskCount);

public sealed record TaskIssueDetailDto(
    Guid Id,
    Guid StudioId,
    Guid? ProjectId,
    string? ProjectName,
    Guid? ParentTaskIssueId,
    TaskIssueKind Kind,
    TaskIssueStatus Status,
    string Title,
    string? Description,
    DateTimeOffset? DueDate,
    TaskMilestoneDto? Milestone,
    IReadOnlyList<TaskAssigneeDto> Assignees,
    IReadOnlyList<TaskReviewerDto> Reviewers,
    IReadOnlyList<TaskLabelDto> Labels,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ClosedAt,
    int CommentsCount,
    int SubtaskCount,
    int CompletedSubtaskCount,
    IReadOnlyList<TaskIssueDto> Subtasks,
    IReadOnlyList<TaskCommentDto> Comments,
    IReadOnlyList<TaskActivityDto> Activity);

public sealed record TaskAssigneeDto(Guid UserId, string Email, string DisplayName);

public sealed record TaskReviewerDto(Guid UserId, string Email, string DisplayName);

public sealed record TaskCommentDto(
    Guid Id,
    Guid TaskIssueId,
    Guid AuthorUserId,
    string AuthorEmail,
    string AuthorDisplayName,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? EditedAt);

public sealed record TaskActivityDto(
    Guid Id,
    Guid TaskIssueId,
    Guid? ActorUserId,
    string? ActorEmail,
    string? ActorDisplayName,
    string Action,
    string Summary,
    string? MetadataJson,
    DateTimeOffset CreatedAt);

public sealed record TaskMilestoneDto(
    Guid Id,
    Guid StudioId,
    string Title,
    string? Description,
    DateTimeOffset? DueDate,
    TaskMilestoneStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TaskLabelDto(
    Guid Id,
    Guid StudioId,
    string Name,
    string Color,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TaskIssueFilters(
    TaskIssueKind? Kind,
    TaskIssueStatus? Status,
    Guid? AssigneeId,
    Guid? MilestoneId,
    Guid? LabelId,
    Guid? ProjectId,
    DateTimeOffset? DueBefore);

public sealed record CreateTaskIssueRequest(
    TaskIssueKind Kind,
    string Title,
    string? Description,
    DateTimeOffset? DueDate,
    Guid? MilestoneId,
    Guid? ProjectId,
    Guid? ParentTaskIssueId,
    IReadOnlyList<Guid>? AssigneeIds,
    IReadOnlyList<Guid>? ReviewerIds,
    IReadOnlyList<Guid>? LabelIds);

public sealed record UpdateTaskIssueRequest(
    TaskIssueKind Kind,
    string Title,
    string? Description,
    DateTimeOffset? DueDate,
    Guid? MilestoneId,
    Guid? ProjectId,
    Guid? ParentTaskIssueId,
    IReadOnlyList<Guid>? AssigneeIds,
    IReadOnlyList<Guid>? ReviewerIds,
    IReadOnlyList<Guid>? LabelIds);

public sealed record UpdateTaskIssueStatusRequest(TaskIssueStatus Status);

public sealed record CreateTaskMilestoneRequest(
    string Title,
    string? Description,
    DateTimeOffset? DueDate,
    TaskMilestoneStatus Status);

public sealed record UpdateTaskMilestoneRequest(
    string Title,
    string? Description,
    DateTimeOffset? DueDate,
    TaskMilestoneStatus Status);

public sealed record CreateTaskLabelRequest(string Name, string Color);

public sealed record UpdateTaskLabelRequest(string Name, string Color);

public sealed record CreateTaskCommentRequest(string Body);

public sealed record UpdateTaskCommentRequest(string Body);
