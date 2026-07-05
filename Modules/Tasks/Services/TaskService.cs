using Kuvox.Api.Modules.Auth.Contracts;
using Kuvox.Api.Modules.Projects.Contracts;
using Kuvox.Api.Modules.Shared.Infrastructure;
using Kuvox.Api.Modules.Tasks.Contracts;
using Kuvox.Api.Modules.Tasks.Models;
using Kuvox.Api.Modules.Tasks.Repositories;
using MediatR;
using System.Text.RegularExpressions;

namespace Kuvox.Api.Modules.Tasks.Services;

internal sealed class TaskService(
    ITaskRepository tasks,
    IAuthApi auth,
    IProjectsApi projects,
    IMediator mediator)
    : ITaskService
{
    private static readonly Regex MentionRegex = new(
        @"(?<![A-Za-z0-9_])@([A-Za-z0-9._%+\-@]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<IReadOnlyList<TaskIssueDto>> ListAsync(
        Guid studioId,
        CallerContext caller,
        TaskIssueFilters filters,
        CancellationToken cancellationToken = default)
    {
        RequireStudioRead(studioId, caller);
        var issues = await tasks.ListByStudioAsync(studioId, filters, cancellationToken);
        return await ToIssueDtosAsync(issues, cancellationToken);
    }

    public async Task<IReadOnlyList<TaskIssueDto>> ListAssignedToMeAsync(
        CallerContext caller,
        TaskIssueFilters filters,
        Guid? studioId,
        CancellationToken cancellationToken = default)
    {
        var studioIds = caller.Studios.Select(studio => studio.StudioId).Distinct().ToList();
        if (studioId is { } sid)
        {
            RequireStudioRead(sid, caller);
            studioIds = studioIds.Where(id => id == sid).ToList();
        }

        var issues = await tasks.ListAssignedToUserAsync(caller.UserId, studioIds, filters, cancellationToken);
        return await ToIssueDtosAsync(issues, cancellationToken);
    }

    public async Task<TaskIssueDetailDto> GetAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var issue = await LoadIssueAsync(id, cancellationToken);
        RequireStudioRead(issue.StudioId, caller);
        return await ToIssueDetailDtoAsync(issue, cancellationToken);
    }

    public async Task<TaskIssueDto> CreateAsync(
        Guid studioId,
        CallerContext caller,
        CreateTaskIssueRequest request,
        CancellationToken cancellationToken = default)
    {
        RequireStudioWrite(studioId, caller);
        ValidateTitle(request.Title);
        await ValidateParentAsync(studioId, null, request.ParentTaskIssueId, cancellationToken);
        await ValidateMilestoneAsync(studioId, request.MilestoneId, cancellationToken);
        var labels = await ValidateLabelsAsync(studioId, request.LabelIds ?? [], cancellationToken);
        var people = await ValidateIssuePeopleAsync(studioId, request.Kind, request.AssigneeIds ?? [], request.ReviewerIds ?? [], cancellationToken);
        await ValidateProjectAsync(studioId, request.ProjectId, cancellationToken);

        var issue = new TaskIssue
        {
            StudioId = studioId,
            ProjectId = request.ProjectId,
            ParentTaskIssueId = request.ParentTaskIssueId,
            Kind = request.Kind,
            Status = TaskIssueStatus.Open,
            Title = request.Title.Trim(),
            Description = TrimOptional(request.Description),
            DueDate = NormalizeDateTimeOffset(request.DueDate),
            MilestoneId = request.MilestoneId,
            CreatedByUserId = caller.UserId,
        };

        foreach (var assigneeId in people.Assignees.Select(member => member.UserId))
        {
            issue.Assignees.Add(new TaskAssignee { TaskIssueId = issue.Id, UserId = assigneeId });
        }

        foreach (var reviewerId in people.Reviewers.Select(member => member.UserId))
        {
            issue.Reviewers.Add(new TaskReviewer { TaskIssueId = issue.Id, UserId = reviewerId });
        }

        foreach (var label in labels)
        {
            issue.Labels.Add(new TaskIssueLabel { TaskIssueId = issue.Id, TaskLabelId = label.Id });
        }

        await tasks.AddIssueAsync(issue, cancellationToken);
        await AddActivityAsync(issue.Id, issue.StudioId, caller.UserId, "created", issue.Kind == TaskIssueKind.Review ? "Created review request." : "Created task.", cancellationToken);
        if (issue.ParentTaskIssueId is { } parentId)
        {
            await AddActivityAsync(parentId, issue.StudioId, caller.UserId, "child_created", $"Created child task \"{issue.Title}\".", cancellationToken);
        }

        await tasks.SaveChangesAsync(cancellationToken);
        await NotifyAssignedAsync(issue, people.Assignees.Select(member => member.UserId), cancellationToken);
        return await ToIssueDtoAsync(issue, cancellationToken);
    }

    public async Task<TaskIssueDto> UpdateAsync(
        Guid id,
        CallerContext caller,
        UpdateTaskIssueRequest request,
        CancellationToken cancellationToken = default)
    {
        var issue = await LoadIssueAsync(id, cancellationToken);
        RequireStudioWrite(issue.StudioId, caller);
        ValidateTitle(request.Title);
        await ValidateParentAsync(issue.StudioId, issue.Id, request.ParentTaskIssueId, cancellationToken);
        await ValidateMilestoneAsync(issue.StudioId, request.MilestoneId, cancellationToken);
        var labels = await ValidateLabelsAsync(issue.StudioId, request.LabelIds ?? [], cancellationToken);
        var people = await ValidateIssuePeopleAsync(issue.StudioId, request.Kind, request.AssigneeIds ?? [], request.ReviewerIds ?? [], cancellationToken);
        await ValidateProjectAsync(issue.StudioId, request.ProjectId, cancellationToken);

        var existingAssigneeIds = issue.Assignees.Select(assignee => assignee.UserId).ToHashSet();
        var nextAssigneeIds = people.Assignees.Select(member => member.UserId).ToHashSet();
        var addedAssigneeIds = nextAssigneeIds.Except(existingAssigneeIds).ToList();
        var existingReviewerIds = issue.Reviewers.Select(reviewer => reviewer.UserId).ToHashSet();
        var nextReviewerIds = people.Reviewers.Select(member => member.UserId).ToHashSet();
        var existingLabelIdsForActivity = issue.Labels.Select(label => label.TaskLabelId).ToHashSet();
        var previousMilestoneId = issue.MilestoneId;
        var previousProjectId = issue.ProjectId;
        var previousParentId = issue.ParentTaskIssueId;

        foreach (var removed in issue.Assignees.Where(assignee => !nextAssigneeIds.Contains(assignee.UserId)).ToList())
        {
            tasks.RemoveAssignee(removed);
        }

        foreach (var added in addedAssigneeIds)
        {
            issue.Assignees.Add(new TaskAssignee { TaskIssueId = issue.Id, UserId = added });
        }

        foreach (var removed in issue.Reviewers.Where(reviewer => !nextReviewerIds.Contains(reviewer.UserId)).ToList())
        {
            tasks.RemoveReviewer(removed);
        }

        foreach (var added in nextReviewerIds.Except(existingReviewerIds))
        {
            issue.Reviewers.Add(new TaskReviewer { TaskIssueId = issue.Id, UserId = added });
        }

        var nextLabelIds = labels.Select(label => label.Id).ToHashSet();
        foreach (var removed in issue.Labels.Where(label => !nextLabelIds.Contains(label.TaskLabelId)).ToList())
        {
            tasks.RemoveIssueLabel(removed);
        }

        var existingLabelIds = issue.Labels.Select(label => label.TaskLabelId).ToHashSet();
        foreach (var added in nextLabelIds.Except(existingLabelIds))
        {
            issue.Labels.Add(new TaskIssueLabel { TaskIssueId = issue.Id, TaskLabelId = added });
        }

        issue.Kind = request.Kind;
        issue.Title = request.Title.Trim();
        issue.Description = TrimOptional(request.Description);
        issue.DueDate = NormalizeDateTimeOffset(request.DueDate);
        issue.MilestoneId = request.MilestoneId;
        issue.ProjectId = request.ProjectId;
        issue.ParentTaskIssueId = request.ParentTaskIssueId;
        issue.UpdatedAt = DateTimeOffset.UtcNow;

        await AddActivityAsync(issue.Id, issue.StudioId, caller.UserId, "edited", "Updated task details.", cancellationToken);
        if (!existingAssigneeIds.SetEquals(nextAssigneeIds))
        {
            await AddActivityAsync(issue.Id, issue.StudioId, caller.UserId, "assignment_changed", "Updated assignees.", cancellationToken);
        }

        if (!existingReviewerIds.SetEquals(nextReviewerIds))
        {
            await AddActivityAsync(issue.Id, issue.StudioId, caller.UserId, "reviewers_changed", "Updated reviewers.", cancellationToken);
        }

        if (!existingLabelIdsForActivity.SetEquals(nextLabelIds))
        {
            await AddActivityAsync(issue.Id, issue.StudioId, caller.UserId, "labels_changed", "Updated labels.", cancellationToken);
        }

        if (previousMilestoneId != issue.MilestoneId)
        {
            await AddActivityAsync(issue.Id, issue.StudioId, caller.UserId, "milestone_changed", "Updated milestone.", cancellationToken);
        }

        if (previousProjectId != issue.ProjectId)
        {
            await AddActivityAsync(issue.Id, issue.StudioId, caller.UserId, "project_changed", "Updated project link.", cancellationToken);
        }

        if (previousParentId != issue.ParentTaskIssueId)
        {
            await AddActivityAsync(issue.Id, issue.StudioId, caller.UserId, "parent_changed", "Updated parent task.", cancellationToken);
        }

        if (issue.ParentTaskIssueId is { } parentId)
        {
            await AddActivityAsync(parentId, issue.StudioId, caller.UserId, "child_updated", $"Updated child task \"{issue.Title}\".", cancellationToken);
        }

        await tasks.SaveChangesAsync(cancellationToken);
        await NotifyAssignedAsync(issue, addedAssigneeIds, cancellationToken);
        return await ToIssueDtoAsync(issue, cancellationToken);
    }

    public async Task<TaskIssueDto> UpdateStatusAsync(
        Guid id,
        CallerContext caller,
        UpdateTaskIssueStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var issue = await LoadIssueAsync(id, cancellationToken);
        RequireStudioWrite(issue.StudioId, caller);
        var previousStatus = issue.Status;
        issue.Status = request.Status;
        issue.ClosedAt = request.Status is TaskIssueStatus.Done or TaskIssueStatus.Closed
            ? DateTimeOffset.UtcNow
            : null;
        issue.UpdatedAt = DateTimeOffset.UtcNow;
        if (previousStatus != issue.Status)
        {
            await AddActivityAsync(issue.Id, issue.StudioId, caller.UserId, "status_changed", $"Changed status from {previousStatus} to {issue.Status}.", cancellationToken);
            if (issue.ParentTaskIssueId is { } parentId)
            {
                await AddActivityAsync(parentId, issue.StudioId, caller.UserId, "child_updated", $"Updated child task \"{issue.Title}\".", cancellationToken);
            }
        }

        await tasks.SaveChangesAsync(cancellationToken);

        if (issue.Kind == TaskIssueKind.Review
            && previousStatus != issue.Status
            && issue.Status is TaskIssueStatus.ChangesRequested or TaskIssueStatus.Approved)
        {
            await NotifyReviewStatusAsync(issue, cancellationToken);
        }

        return await ToIssueDtoAsync(issue, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var issue = await LoadIssueAsync(id, cancellationToken);
        RequireStudioWrite(issue.StudioId, caller);
        if (issue.ParentTaskIssueId is { } parentId)
        {
            await AddActivityAsync(parentId, issue.StudioId, caller.UserId, "child_deleted", $"Deleted child task \"{issue.Title}\".", cancellationToken);
        }

        await DeleteIssueSubtreeAsync(issue, cancellationToken);
        await tasks.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TaskMilestoneDto>> ListMilestonesAsync(
        Guid studioId,
        CallerContext caller,
        CancellationToken cancellationToken = default)
    {
        RequireStudioRead(studioId, caller);
        var milestones = await tasks.ListMilestonesAsync(studioId, cancellationToken);
        return milestones.Select(ToMilestoneDto).ToList();
    }

    public async Task<TaskMilestoneDto> CreateMilestoneAsync(
        Guid studioId,
        CallerContext caller,
        CreateTaskMilestoneRequest request,
        CancellationToken cancellationToken = default)
    {
        RequireStudioWrite(studioId, caller);
        ValidateTitle(request.Title);
        ValidateMaxLength(request.Title, 200, "Milestone title");
        ValidateMaxLength(request.Description, 2000, "Milestone description");
        var title = request.Title.Trim();
        if (await tasks.MilestoneTitleExistsAsync(studioId, title, cancellationToken: cancellationToken))
        {
            throw DomainException.Conflict("A milestone with this title already exists in this Studio.");
        }

        var milestone = new TaskMilestone
        {
            StudioId = studioId,
            Title = title,
            Description = TrimOptional(request.Description),
            DueDate = NormalizeDateTimeOffset(request.DueDate),
            Status = request.Status,
        };
        await tasks.AddMilestoneAsync(milestone, cancellationToken);
        await tasks.SaveChangesAsync(cancellationToken);
        return ToMilestoneDto(milestone);
    }

    public async Task DeleteMilestoneAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var milestone = await tasks.GetMilestoneAsync(id, cancellationToken)
            ?? throw DomainException.NotFound("Milestone not found.");
        RequireStudioWrite(milestone.StudioId, caller);
        var affectedIssues = await tasks.ListIssuesByMilestoneAsync(milestone.Id, cancellationToken);
        foreach (var issue in affectedIssues)
        {
            issue.MilestoneId = null;
            issue.UpdatedAt = DateTimeOffset.UtcNow;
            await AddActivityAsync(issue.Id, issue.StudioId, caller.UserId, "milestone_changed", $"Removed deleted milestone \"{milestone.Title}\".", cancellationToken);
        }

        tasks.RemoveMilestone(milestone);
        await tasks.SaveChangesAsync(cancellationToken);
    }

    public async Task<TaskMilestoneDto> UpdateMilestoneAsync(
        Guid id,
        CallerContext caller,
        UpdateTaskMilestoneRequest request,
        CancellationToken cancellationToken = default)
    {
        var milestone = await tasks.GetMilestoneAsync(id, cancellationToken)
            ?? throw DomainException.NotFound("Milestone not found.");
        RequireStudioWrite(milestone.StudioId, caller);
        ValidateTitle(request.Title);
        ValidateMaxLength(request.Title, 200, "Milestone title");
        ValidateMaxLength(request.Description, 2000, "Milestone description");
        var title = request.Title.Trim();
        if (await tasks.MilestoneTitleExistsAsync(milestone.StudioId, title, milestone.Id, cancellationToken))
        {
            throw DomainException.Conflict("A milestone with this title already exists in this Studio.");
        }

        milestone.Title = title;
        milestone.Description = TrimOptional(request.Description);
        milestone.DueDate = NormalizeDateTimeOffset(request.DueDate);
        milestone.Status = request.Status;
        milestone.UpdatedAt = DateTimeOffset.UtcNow;
        await tasks.SaveChangesAsync(cancellationToken);
        return ToMilestoneDto(milestone);
    }

    public async Task<IReadOnlyList<TaskLabelDto>> ListLabelsAsync(
        Guid studioId,
        CallerContext caller,
        CancellationToken cancellationToken = default)
    {
        RequireStudioRead(studioId, caller);
        var labels = await tasks.ListLabelsAsync(studioId, cancellationToken);
        return labels.Select(ToLabelDto).ToList();
    }

    public async Task<TaskLabelDto> CreateLabelAsync(
        Guid studioId,
        CallerContext caller,
        CreateTaskLabelRequest request,
        CancellationToken cancellationToken = default)
    {
        RequireStudioWrite(studioId, caller);
        ValidateTitle(request.Name);
        ValidateMaxLength(request.Name, 80, "Label name");
        var name = request.Name.Trim();
        if (await tasks.LabelNameExistsAsync(studioId, name, cancellationToken: cancellationToken))
        {
            throw DomainException.Conflict("A label with this name already exists in this Studio.");
        }

        var label = new TaskLabel
        {
            StudioId = studioId,
            Name = name,
            Color = NormalizeColor(request.Color),
        };
        await tasks.AddLabelAsync(label, cancellationToken);
        await tasks.SaveChangesAsync(cancellationToken);
        return ToLabelDto(label);
    }

    public async Task DeleteLabelAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var label = await tasks.GetLabelAsync(id, cancellationToken)
            ?? throw DomainException.NotFound("Label not found.");
        RequireStudioWrite(label.StudioId, caller);
        var affectedIssues = await tasks.ListIssuesByLabelAsync(label.Id, cancellationToken);
        foreach (var issue in affectedIssues)
        {
            foreach (var issueLabel in issue.Labels.Where(issueLabel => issueLabel.TaskLabelId == label.Id).ToList())
            {
                tasks.RemoveIssueLabel(issueLabel);
            }

            issue.UpdatedAt = DateTimeOffset.UtcNow;
            await AddActivityAsync(issue.Id, issue.StudioId, caller.UserId, "labels_changed", $"Removed deleted label \"{label.Name}\".", cancellationToken);
        }

        tasks.RemoveLabel(label);
        await tasks.SaveChangesAsync(cancellationToken);
    }

    public async Task<TaskCommentDto> CreateCommentAsync(
        Guid taskIssueId,
        CallerContext caller,
        CreateTaskCommentRequest request,
        CancellationToken cancellationToken = default)
    {
        var issue = await LoadIssueAsync(taskIssueId, cancellationToken);
        RequireStudioRead(issue.StudioId, caller);
        ValidateCommentBody(request.Body);
        var comment = new TaskComment
        {
            StudioId = issue.StudioId,
            TaskIssueId = issue.Id,
            AuthorUserId = caller.UserId,
            Body = request.Body.Trim(),
        };
        await tasks.AddCommentAsync(comment, cancellationToken);
        issue.UpdatedAt = DateTimeOffset.UtcNow;
        await AddActivityAsync(issue.Id, issue.StudioId, caller.UserId, "comment_created", "Added a comment.", cancellationToken);
        await tasks.SaveChangesAsync(cancellationToken);
        await NotifyCommentMentionsAsync(issue, caller.UserId, comment.Body, previousBody: null, cancellationToken);
        return await ToCommentDtoAsync(comment, cancellationToken);
    }

    public async Task<TaskCommentDto> UpdateCommentAsync(
        Guid commentId,
        CallerContext caller,
        UpdateTaskCommentRequest request,
        CancellationToken cancellationToken = default)
    {
        var comment = await tasks.GetCommentAsync(commentId, cancellationToken)
            ?? throw DomainException.NotFound("Comment not found.");
        RequireStudioRead(comment.StudioId, caller);
        if (comment.AuthorUserId != caller.UserId)
        {
            throw DomainException.Forbidden("Only the comment author can edit this comment.");
        }

        ValidateCommentBody(request.Body);
        var previousBody = comment.Body;
        comment.Body = request.Body.Trim();
        comment.EditedAt = DateTimeOffset.UtcNow;
        comment.UpdatedAt = DateTimeOffset.UtcNow;
        var issue = comment.TaskIssue ?? await LoadIssueAsync(comment.TaskIssueId, cancellationToken);
        if (comment.TaskIssue is not null)
        {
            comment.TaskIssue.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await AddActivityAsync(comment.TaskIssueId, comment.StudioId, caller.UserId, "comment_edited", "Edited a comment.", cancellationToken);
        await tasks.SaveChangesAsync(cancellationToken);
        await NotifyCommentMentionsAsync(issue, caller.UserId, comment.Body, previousBody, cancellationToken);
        return await ToCommentDtoAsync(comment, cancellationToken);
    }

    public async Task DeleteCommentAsync(Guid commentId, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var comment = await tasks.GetCommentAsync(commentId, cancellationToken)
            ?? throw DomainException.NotFound("Comment not found.");
        RequireStudioRead(comment.StudioId, caller);
        if (comment.AuthorUserId != caller.UserId && !caller.IsStudioAdmin(comment.StudioId))
        {
            throw DomainException.Forbidden("You do not have permission to delete this comment.");
        }

        var taskIssueId = comment.TaskIssueId;
        var studioId = comment.StudioId;
        if (comment.TaskIssue is not null)
        {
            comment.TaskIssue.UpdatedAt = DateTimeOffset.UtcNow;
        }

        tasks.RemoveComment(comment);
        await AddActivityAsync(taskIssueId, studioId, caller.UserId, "comment_deleted", "Deleted a comment.", cancellationToken);
        await tasks.SaveChangesAsync(cancellationToken);
    }

    public async Task<TaskLabelDto> UpdateLabelAsync(
        Guid id,
        CallerContext caller,
        UpdateTaskLabelRequest request,
        CancellationToken cancellationToken = default)
    {
        var label = await tasks.GetLabelAsync(id, cancellationToken)
            ?? throw DomainException.NotFound("Label not found.");
        RequireStudioWrite(label.StudioId, caller);
        ValidateTitle(request.Name);
        ValidateMaxLength(request.Name, 80, "Label name");
        var name = request.Name.Trim();
        if (await tasks.LabelNameExistsAsync(label.StudioId, name, label.Id, cancellationToken))
        {
            throw DomainException.Conflict("A label with this name already exists in this Studio.");
        }

        label.Name = name;
        label.Color = NormalizeColor(request.Color);
        label.UpdatedAt = DateTimeOffset.UtcNow;
        await tasks.SaveChangesAsync(cancellationToken);
        return ToLabelDto(label);
    }

    private async Task<TaskIssue> LoadIssueAsync(Guid id, CancellationToken cancellationToken) =>
        await tasks.GetIssueAsync(id, cancellationToken)
        ?? throw DomainException.NotFound("Task not found.");

    private static void RequireStudioRead(Guid studioId, CallerContext caller)
    {
        if (!caller.InStudio(studioId))
        {
            throw DomainException.Forbidden("You are not a member of this studio.");
        }
    }

    private static void RequireStudioWrite(Guid studioId, CallerContext caller)
    {
        RequireStudioRead(studioId, caller);
        if (!caller.CanWriteStudioContent(studioId))
        {
            throw DomainException.Forbidden("You do not have permission to modify Studio tasks.");
        }
    }

    private async Task ValidateMilestoneAsync(Guid studioId, Guid? milestoneId, CancellationToken cancellationToken)
    {
        if (milestoneId is not { } id)
        {
            return;
        }

        var milestone = await tasks.GetMilestoneAsync(id, cancellationToken)
            ?? throw DomainException.BadRequest("Milestone not found.");
        if (milestone.StudioId != studioId)
        {
            throw DomainException.BadRequest("Milestone belongs to a different Studio.");
        }
    }

    private async Task ValidateParentAsync(
        Guid studioId,
        Guid? taskIssueId,
        Guid? parentTaskIssueId,
        CancellationToken cancellationToken)
    {
        if (parentTaskIssueId is not { } parentId)
        {
            return;
        }

        if (taskIssueId == parentId)
        {
            throw DomainException.BadRequest("A task cannot be its own parent.");
        }

        var parent = await tasks.GetIssueAsync(parentId, cancellationToken)
            ?? throw DomainException.BadRequest("Parent task not found.");
        if (parent.StudioId != studioId)
        {
            throw DomainException.BadRequest("Parent task belongs to a different Studio.");
        }

        var ancestorId = parent.ParentTaskIssueId;
        while (ancestorId is { } currentAncestorId)
        {
            if (taskIssueId == currentAncestorId)
            {
                throw DomainException.BadRequest("A task cannot be parented under one of its descendants.");
            }

            var ancestor = await tasks.GetIssueAsync(currentAncestorId, cancellationToken);
            ancestorId = ancestor?.ParentTaskIssueId;
        }
    }

    private async Task<IReadOnlyList<TaskLabel>> ValidateLabelsAsync(
        Guid studioId,
        IReadOnlyCollection<Guid> labelIds,
        CancellationToken cancellationToken)
    {
        var ids = labelIds.Distinct().ToArray();
        var labels = await tasks.GetLabelsAsync(ids, cancellationToken);
        if (labels.Count != ids.Length)
        {
            throw DomainException.BadRequest("One or more labels were not found.");
        }

        if (labels.Any(label => label.StudioId != studioId))
        {
            throw DomainException.BadRequest("One or more labels belong to a different Studio.");
        }

        return labels;
    }

    private async Task<IReadOnlyList<StudioMemberSummary>> ValidateAssigneesAsync(
        Guid studioId,
        IReadOnlyCollection<Guid> assigneeIds,
        CancellationToken cancellationToken)
    {
        var ids = assigneeIds.Distinct().ToHashSet();
        if (ids.Count == 0)
        {
            return [];
        }

        var members = await auth.ListStudioMembersAsync(studioId, cancellationToken);
        var selected = members.Where(member => ids.Contains(member.UserId)).ToList();
        if (selected.Count != ids.Count)
        {
            throw DomainException.BadRequest("Assignees must be members of this Studio.");
        }

        return selected;
    }

    private async Task<IssuePeople> ValidateIssuePeopleAsync(
        Guid studioId,
        TaskIssueKind kind,
        IReadOnlyCollection<Guid> assigneeIds,
        IReadOnlyCollection<Guid> reviewerIds,
        CancellationToken cancellationToken)
    {
        if (kind == TaskIssueKind.Review)
        {
            var reviewers = await ValidateReviewersAsync(studioId, reviewerIds, requireAny: true, cancellationToken);
            return new IssuePeople([], reviewers);
        }

        var assignees = await ValidateAssigneesAsync(studioId, assigneeIds, cancellationToken);
        var taskReviewers = await ValidateReviewersAsync(studioId, reviewerIds, requireAny: false, cancellationToken);
        return new IssuePeople(assignees, taskReviewers);
    }

    private async Task<IReadOnlyList<StudioMemberSummary>> ValidateReviewersAsync(
        Guid studioId,
        IReadOnlyCollection<Guid> reviewerIds,
        bool requireAny,
        CancellationToken cancellationToken)
    {
        var ids = reviewerIds.Distinct().ToHashSet();
        if (ids.Count == 0)
        {
            if (!requireAny)
            {
                return [];
            }

            throw DomainException.BadRequest("Reviews require at least one reviewer.");
        }

        var members = await auth.ListStudioMembersAsync(studioId, cancellationToken);
        var selected = members.Where(member => ids.Contains(member.UserId)).ToList();
        if (selected.Count != ids.Count)
        {
            throw DomainException.BadRequest("Reviewers must be members of this Studio.");
        }

        return selected;
    }

    private async Task ValidateProjectAsync(Guid studioId, Guid? projectId, CancellationToken cancellationToken)
    {
        if (projectId is not { } id)
        {
            return;
        }

        var project = await projects.GetSummaryAsync(id, cancellationToken)
            ?? throw DomainException.BadRequest("Project not found.");
        if (project.OwnerKind != ProjectOwnerKind.Studio || project.OwnerId != studioId)
        {
            throw DomainException.BadRequest("Linked project must belong to the same Studio.");
        }
    }

    private async Task<IReadOnlyList<TaskIssueDto>> ToIssueDtosAsync(
        IReadOnlyList<TaskIssue> issues,
        CancellationToken cancellationToken)
    {
        var items = new List<TaskIssueDto>();
        foreach (var issue in issues)
        {
            items.Add(await ToIssueDtoAsync(issue, cancellationToken));
        }

        return items;
    }

    private async Task<TaskIssueDetailDto> ToIssueDetailDtoAsync(TaskIssue issue, CancellationToken cancellationToken)
    {
        var dto = await ToIssueDtoAsync(issue, cancellationToken);
        var subtasks = await ToIssueDtosAsync(await tasks.ListChildIssuesAsync(issue.Id, cancellationToken), cancellationToken);
        var members = await auth.ListStudioMembersAsync(issue.StudioId, cancellationToken);
        var memberMap = members.ToDictionary(member => member.UserId);
        return new TaskIssueDetailDto(
            dto.Id,
            dto.StudioId,
            dto.ProjectId,
            dto.ProjectName,
            dto.ParentTaskIssueId,
            dto.Kind,
            dto.Status,
            dto.Title,
            dto.Description,
            dto.DueDate,
            dto.Milestone,
            dto.Assignees,
            dto.Reviewers,
            dto.Labels,
            dto.CreatedByUserId,
            dto.CreatedAt,
            dto.UpdatedAt,
            dto.ClosedAt,
            dto.CommentsCount,
            dto.SubtaskCount,
            dto.CompletedSubtaskCount,
            subtasks,
            issue.Comments
                .OrderBy(comment => comment.CreatedAt)
                .Select(comment => ToCommentDto(comment, memberMap))
                .ToList(),
            issue.Activities
                .OrderByDescending(activity => activity.CreatedAt)
                .Select(activity => ToActivityDto(activity, memberMap))
                .ToList());
    }

    private async Task<TaskIssueDto> ToIssueDtoAsync(TaskIssue issue, CancellationToken cancellationToken)
    {
        var members = await auth.ListStudioMembersAsync(issue.StudioId, cancellationToken);
        var memberMap = members.ToDictionary(member => member.UserId);
        string? projectName = null;
        if (issue.ProjectId is { } projectId)
        {
            projectName = (await projects.GetSummaryAsync(projectId, cancellationToken))?.Name;
        }

        return new TaskIssueDto(
            issue.Id,
            issue.StudioId,
            issue.ProjectId,
            projectName,
            issue.ParentTaskIssueId,
            issue.Kind,
            issue.Status,
            issue.Title,
            issue.Description,
            issue.DueDate,
            issue.Milestone is null ? null : ToMilestoneDto(issue.Milestone),
            issue.Assignees
                .OrderBy(assignee => memberMap.GetValueOrDefault(assignee.UserId)?.DisplayName ?? "")
                .Select(assignee => ToAssigneeDto(assignee.UserId, memberMap))
                .ToList(),
            issue.Reviewers
                .OrderBy(reviewer => memberMap.GetValueOrDefault(reviewer.UserId)?.DisplayName ?? "")
                .Select(reviewer => ToReviewerDto(reviewer.UserId, memberMap))
                .ToList(),
            issue.Labels
                .Where(label => label.TaskLabel is not null)
                .OrderBy(label => label.TaskLabel!.Name)
                .Select(label => ToLabelDto(label.TaskLabel!))
                .ToList(),
            issue.CreatedByUserId,
            issue.CreatedAt,
            issue.UpdatedAt,
            issue.ClosedAt,
            issue.Comments.Count,
            issue.Children.Count,
            issue.Children.Count(child => child.Status is TaskIssueStatus.Done or TaskIssueStatus.Closed));
    }

    private async Task<TaskCommentDto> ToCommentDtoAsync(TaskComment comment, CancellationToken cancellationToken)
    {
        var members = await auth.ListStudioMembersAsync(comment.StudioId, cancellationToken);
        return ToCommentDto(comment, members.ToDictionary(member => member.UserId));
    }

    private static TaskAssigneeDto ToAssigneeDto(
        Guid userId,
        IReadOnlyDictionary<Guid, StudioMemberSummary> memberMap)
    {
        if (memberMap.TryGetValue(userId, out var member))
        {
            return new TaskAssigneeDto(member.UserId, member.Email, member.DisplayName);
        }

        return new TaskAssigneeDto(userId, "", "Former member");
    }

    private static TaskReviewerDto ToReviewerDto(
        Guid userId,
        IReadOnlyDictionary<Guid, StudioMemberSummary> memberMap)
    {
        if (memberMap.TryGetValue(userId, out var member))
        {
            return new TaskReviewerDto(member.UserId, member.Email, member.DisplayName);
        }

        return new TaskReviewerDto(userId, "", "Former member");
    }

    private static TaskCommentDto ToCommentDto(
        TaskComment comment,
        IReadOnlyDictionary<Guid, StudioMemberSummary> memberMap)
    {
        var author = memberMap.GetValueOrDefault(comment.AuthorUserId);
        return new TaskCommentDto(
            comment.Id,
            comment.TaskIssueId,
            comment.AuthorUserId,
            author?.Email ?? "",
            author?.DisplayName ?? "Former member",
            comment.Body,
            comment.CreatedAt,
            comment.UpdatedAt,
            comment.EditedAt);
    }

    private static TaskActivityDto ToActivityDto(
        TaskActivity activity,
        IReadOnlyDictionary<Guid, StudioMemberSummary> memberMap)
    {
        StudioMemberSummary? actor = null;
        if (activity.ActorUserId is { } actorUserId)
        {
            memberMap.TryGetValue(actorUserId, out actor);
        }

        return new TaskActivityDto(
            activity.Id,
            activity.TaskIssueId,
            activity.ActorUserId,
            actor?.Email,
            activity.ActorUserId is null ? null : actor?.DisplayName ?? "Former member",
            activity.Action,
            activity.Summary,
            activity.MetadataJson,
            activity.CreatedAt);
    }

    private async Task AddActivityAsync(
        Guid taskIssueId,
        Guid studioId,
        Guid? actorUserId,
        string action,
        string summary,
        CancellationToken cancellationToken)
    {
        await tasks.AddActivityAsync(
            new TaskActivity
            {
                StudioId = studioId,
                TaskIssueId = taskIssueId,
                ActorUserId = actorUserId,
                Action = action,
                Summary = summary,
            },
            cancellationToken);
    }

    private async Task DeleteIssueSubtreeAsync(TaskIssue issue, CancellationToken cancellationToken)
    {
        var children = await tasks.ListChildIssuesAsync(issue.Id, cancellationToken);
        foreach (var child in children)
        {
            await DeleteIssueSubtreeAsync(child, cancellationToken);
        }

        tasks.RemoveIssue(issue);
    }

    private async Task NotifyAssignedAsync(
        TaskIssue issue,
        IEnumerable<Guid> assigneeIds,
        CancellationToken cancellationToken)
    {
        if (issue.Kind != TaskIssueKind.Task)
        {
            return;
        }

        var distinctAssigneeIds = assigneeIds.Distinct().ToArray();
        if (distinctAssigneeIds.Length == 0)
        {
            return;
        }

        await mediator.Publish(
            new TaskAssignedEvent(
                issue.Id,
                issue.StudioId,
                issue.Kind,
                issue.Title,
                distinctAssigneeIds),
            cancellationToken);
    }

    private async Task NotifyReviewStatusAsync(TaskIssue issue, CancellationToken cancellationToken)
    {
        var reviewerIds = issue.Reviewers.Select(reviewer => reviewer.UserId).Distinct().ToArray();
        if (reviewerIds.Length == 0)
        {
            return;
        }

        await mediator.Publish(
            new TaskReviewStatusChangedEvent(
                issue.Id,
                issue.StudioId,
                issue.Title,
                issue.Status,
                reviewerIds),
            cancellationToken);
    }

    private async Task NotifyCommentMentionsAsync(
        TaskIssue issue,
        Guid authorUserId,
        string body,
        string? previousBody,
        CancellationToken cancellationToken)
    {
        var members = await auth.ListStudioMembersAsync(issue.StudioId, cancellationToken);
        var mentionedUserIds = ResolveMentionedUserIds(body, members);
        if (previousBody is not null)
        {
            mentionedUserIds.ExceptWith(ResolveMentionedUserIds(previousBody, members));
        }

        mentionedUserIds.Remove(authorUserId);
        if (mentionedUserIds.Count == 0)
        {
            return;
        }

        var author = members.FirstOrDefault(member => member.UserId == authorUserId);
        await mediator.Publish(
            new TaskCommentMentionedEvent(
                issue.Id,
                issue.StudioId,
                issue.Title,
                authorUserId,
                author?.DisplayName ?? "Someone",
                mentionedUserIds.ToArray()),
            cancellationToken);
    }

    private static HashSet<Guid> ResolveMentionedUserIds(string body, IReadOnlyList<StudioMemberSummary> members)
    {
        var aliases = new Dictionary<string, HashSet<Guid>>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in members)
        {
            AddMentionAlias(aliases, member.Email, member.UserId);
            AddMentionAlias(aliases, member.Email.Split('@')[0], member.UserId);
            AddMentionAlias(aliases, MentionSlug(member.DisplayName), member.UserId);
        }

        var result = new HashSet<Guid>();
        foreach (Match match in MentionRegex.Matches(body))
        {
            var token = match.Groups[1].Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '}');
            if (aliases.TryGetValue(token, out var users) && users.Count == 1)
            {
                result.Add(users.Single());
            }
        }

        return result;
    }

    private static void AddMentionAlias(IDictionary<string, HashSet<Guid>> aliases, string? alias, Guid userId)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return;
        }

        var normalized = alias.Trim().ToLowerInvariant();
        if (!aliases.TryGetValue(normalized, out var users))
        {
            users = [];
            aliases[normalized] = users;
        }

        users.Add(userId);
    }

    private static string MentionSlug(string value)
    {
        var slug = Regex.Replace(value.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-");
        return slug.Trim('-');
    }

    private static TaskMilestoneDto ToMilestoneDto(TaskMilestone milestone) =>
        new(
            milestone.Id,
            milestone.StudioId,
            milestone.Title,
            milestone.Description,
            milestone.DueDate,
            milestone.Status,
            milestone.CreatedAt,
            milestone.UpdatedAt);

    private static TaskLabelDto ToLabelDto(TaskLabel label) =>
        new(label.Id, label.StudioId, label.Name, label.Color, label.CreatedAt, label.UpdatedAt);

    private static void ValidateTitle(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw DomainException.BadRequest("Title is required.");
        }
    }

    private static void ValidateCommentBody(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw DomainException.BadRequest("Comment body is required.");
        }

        ValidateMaxLength(value, 4000, "Comment");
    }

    private static void ValidateMaxLength(string? value, int maxLength, string fieldName)
    {
        if (value?.Trim().Length > maxLength)
        {
            throw DomainException.BadRequest($"{fieldName} must be {maxLength} characters or fewer.");
        }
    }

    private static string? TrimOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static DateTimeOffset? NormalizeDateTimeOffset(DateTimeOffset? value) =>
        value?.ToUniversalTime();

    private static string NormalizeColor(string value)
    {
        var color = value.Trim();
        return string.IsNullOrWhiteSpace(color) ? "#5B6CFF" : color;
    }

    private sealed record IssuePeople(
        IReadOnlyList<StudioMemberSummary> Assignees,
        IReadOnlyList<StudioMemberSummary> Reviewers);

}
