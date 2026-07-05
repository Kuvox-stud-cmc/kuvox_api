using Kuvox.Api.Modules.Tasks.Contracts;
using Kuvox.Api.Modules.Tasks.Models;
using Microsoft.EntityFrameworkCore;

namespace Kuvox.Api.Modules.Tasks.Repositories;

internal sealed class TaskRepository(TasksDbContext db) : ITaskRepository
{
    public async Task<IReadOnlyList<TaskIssue>> ListByStudioAsync(
        Guid studioId,
        TaskIssueFilters filters,
        CancellationToken cancellationToken = default) =>
        await ApplyFilters(BaseIssueQuery().Where(issue => issue.StudioId == studioId), filters)
            .OrderBy(issue => issue.Status == TaskIssueStatus.Closed)
            .ThenBy(issue => issue.DueDate == null)
            .ThenBy(issue => issue.DueDate)
            .ThenByDescending(issue => issue.UpdatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TaskIssue>> ListAssignedToUserAsync(
        Guid userId,
        IReadOnlyCollection<Guid> studioIds,
        TaskIssueFilters filters,
        CancellationToken cancellationToken = default)
    {
        if (studioIds.Count == 0)
        {
            return [];
        }

        return await ApplyFilters(
                BaseIssueQuery()
                    .Where(issue => studioIds.Contains(issue.StudioId))
                    .Where(issue => issue.Assignees.Any(assignee => assignee.UserId == userId)),
                filters)
            .OrderBy(issue => issue.Status == TaskIssueStatus.Closed)
            .ThenBy(issue => issue.DueDate == null)
            .ThenBy(issue => issue.DueDate)
            .ThenByDescending(issue => issue.UpdatedAt)
            .ToListAsync(cancellationToken);
    }

    public Task<TaskIssue?> GetIssueAsync(Guid id, CancellationToken cancellationToken = default) =>
        BaseIssueQuery().FirstOrDefaultAsync(issue => issue.Id == id, cancellationToken);

    public async Task<IReadOnlyList<TaskIssue>> ListChildIssuesAsync(Guid parentTaskIssueId, CancellationToken cancellationToken = default) =>
        await BaseIssueQuery()
            .Where(issue => issue.ParentTaskIssueId == parentTaskIssueId)
            .OrderBy(issue => issue.Status == TaskIssueStatus.Closed)
            .ThenBy(issue => issue.DueDate == null)
            .ThenBy(issue => issue.DueDate)
            .ThenByDescending(issue => issue.UpdatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TaskIssue>> ListIssuesByMilestoneAsync(Guid milestoneId, CancellationToken cancellationToken = default) =>
        await BaseIssueQuery()
            .Where(issue => issue.MilestoneId == milestoneId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TaskIssue>> ListIssuesByLabelAsync(Guid labelId, CancellationToken cancellationToken = default) =>
        await BaseIssueQuery()
            .Where(issue => issue.Labels.Any(taskLabel => taskLabel.TaskLabelId == labelId))
            .ToListAsync(cancellationToken);

    public async Task AddIssueAsync(TaskIssue issue, CancellationToken cancellationToken = default) =>
        await db.Issues.AddAsync(issue, cancellationToken);

    public void RemoveIssue(TaskIssue issue) => db.Issues.Remove(issue);

    public void RemoveAssignee(TaskAssignee assignee) => db.Assignees.Remove(assignee);

    public void RemoveIssueLabel(TaskIssueLabel issueLabel) => db.IssueLabels.Remove(issueLabel);

    public async Task<IReadOnlyList<TaskMilestone>> ListMilestonesAsync(Guid studioId, CancellationToken cancellationToken = default) =>
        await db.Milestones
            .Where(milestone => milestone.StudioId == studioId)
            .OrderBy(milestone => milestone.Status)
            .ThenBy(milestone => milestone.DueDate == null)
            .ThenBy(milestone => milestone.DueDate)
            .ThenBy(milestone => milestone.Title)
            .ToListAsync(cancellationToken);

    public Task<TaskMilestone?> GetMilestoneAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Milestones.FirstOrDefaultAsync(milestone => milestone.Id == id, cancellationToken);

    public Task<bool> MilestoneTitleExistsAsync(
        Guid studioId,
        string title,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default) =>
        db.Milestones.AnyAsync(
            milestone => milestone.StudioId == studioId
                && milestone.Title == title
                && (excludeId == null || milestone.Id != excludeId),
            cancellationToken);

    public async Task AddMilestoneAsync(TaskMilestone milestone, CancellationToken cancellationToken = default) =>
        await db.Milestones.AddAsync(milestone, cancellationToken);

    public void RemoveMilestone(TaskMilestone milestone) => db.Milestones.Remove(milestone);

    public async Task<IReadOnlyList<TaskLabel>> ListLabelsAsync(Guid studioId, CancellationToken cancellationToken = default) =>
        await db.Labels
            .Where(label => label.StudioId == studioId)
            .OrderBy(label => label.Name)
            .ToListAsync(cancellationToken);

    public Task<TaskLabel?> GetLabelAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Labels.FirstOrDefaultAsync(label => label.Id == id, cancellationToken);

    public async Task<IReadOnlyList<TaskLabel>> GetLabelsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.Labels.Where(label => ids.Contains(label.Id)).ToListAsync(cancellationToken);
    }

    public async Task AddLabelAsync(TaskLabel label, CancellationToken cancellationToken = default) =>
        await db.Labels.AddAsync(label, cancellationToken);

    public void RemoveLabel(TaskLabel label) => db.Labels.Remove(label);

    public Task<TaskComment?> GetCommentAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Comments
            .Include(comment => comment.TaskIssue)
            .FirstOrDefaultAsync(comment => comment.Id == id, cancellationToken);

    public async Task AddCommentAsync(TaskComment comment, CancellationToken cancellationToken = default) =>
        await db.Comments.AddAsync(comment, cancellationToken);

    public void RemoveComment(TaskComment comment) => db.Comments.Remove(comment);

    public async Task AddActivityAsync(TaskActivity activity, CancellationToken cancellationToken = default) =>
        await db.Activities.AddAsync(activity, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);

    private IQueryable<TaskIssue> BaseIssueQuery() =>
        db.Issues
            .Include(issue => issue.Milestone)
            .Include(issue => issue.Children)
            .Include(issue => issue.Comments)
            .Include(issue => issue.Activities)
            .Include(issue => issue.Assignees)
            .Include(issue => issue.Labels)
                .ThenInclude(issueLabel => issueLabel.TaskLabel);

    private static IQueryable<TaskIssue> ApplyFilters(IQueryable<TaskIssue> query, TaskIssueFilters filters)
    {
        if (filters.Kind is { } kind)
        {
            query = query.Where(issue => issue.Kind == kind);
        }

        if (filters.Status is { } status)
        {
            query = query.Where(issue => issue.Status == status);
        }

        if (filters.AssigneeId is { } assigneeId)
        {
            query = query.Where(issue => issue.Assignees.Any(assignee => assignee.UserId == assigneeId));
        }

        if (filters.MilestoneId is { } milestoneId)
        {
            query = query.Where(issue => issue.MilestoneId == milestoneId);
        }

        if (filters.LabelId is { } labelId)
        {
            query = query.Where(issue => issue.Labels.Any(label => label.TaskLabelId == labelId));
        }

        if (filters.ProjectId is { } projectId)
        {
            query = query.Where(issue => issue.ProjectId == projectId);
        }

        if (filters.DueBefore is { } dueBefore)
        {
            query = query.Where(issue => issue.DueDate != null && issue.DueDate <= dueBefore);
        }

        return query;
    }
}
