using Kuvox.Api.Modules.Tasks.Contracts;
using Kuvox.Api.Modules.Tasks.Models;

namespace Kuvox.Api.Modules.Tasks.Repositories;

internal interface ITaskRepository
{
    Task<IReadOnlyList<TaskIssue>> ListByStudioAsync(Guid studioId, TaskIssueFilters filters, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskIssue>> ListAssignedToUserAsync(Guid userId, IReadOnlyCollection<Guid> studioIds, TaskIssueFilters filters, CancellationToken cancellationToken = default);

    Task<TaskIssue?> GetIssueAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskIssue>> ListChildIssuesAsync(Guid parentTaskIssueId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskIssue>> ListIssuesByMilestoneAsync(Guid milestoneId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskIssue>> ListIssuesByLabelAsync(Guid labelId, CancellationToken cancellationToken = default);

    Task AddIssueAsync(TaskIssue issue, CancellationToken cancellationToken = default);

    void RemoveIssue(TaskIssue issue);

    void RemoveAssignee(TaskAssignee assignee);

    void RemoveIssueLabel(TaskIssueLabel issueLabel);

    Task<IReadOnlyList<TaskMilestone>> ListMilestonesAsync(Guid studioId, CancellationToken cancellationToken = default);

    Task<TaskMilestone?> GetMilestoneAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> MilestoneTitleExistsAsync(Guid studioId, string title, Guid? excludeId = null, CancellationToken cancellationToken = default);

    Task AddMilestoneAsync(TaskMilestone milestone, CancellationToken cancellationToken = default);

    void RemoveMilestone(TaskMilestone milestone);

    Task<IReadOnlyList<TaskLabel>> ListLabelsAsync(Guid studioId, CancellationToken cancellationToken = default);

    Task<TaskLabel?> GetLabelAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskLabel>> GetLabelsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    Task AddLabelAsync(TaskLabel label, CancellationToken cancellationToken = default);

    void RemoveLabel(TaskLabel label);

    Task<TaskComment?> GetCommentAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddCommentAsync(TaskComment comment, CancellationToken cancellationToken = default);

    void RemoveComment(TaskComment comment);

    Task AddActivityAsync(TaskActivity activity, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
