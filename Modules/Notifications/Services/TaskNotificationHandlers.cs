using Kuvox.Api.Modules.Notifications.Enums;
using Kuvox.Api.Modules.Notifications.Models;
using Kuvox.Api.Modules.Notifications.Repositories;
using Kuvox.Api.Modules.Tasks.Contracts;
using MediatR;

namespace Kuvox.Api.Modules.Notifications.Services;

internal sealed class TaskAssignedHandler(
    INotificationsRepository notifications,
    NotificationCacheInvalidator invalidator)
    : INotificationHandler<TaskAssignedEvent>
{
    public async Task Handle(TaskAssignedEvent notification, CancellationToken cancellationToken)
    {
        foreach (var assigneeId in notification.AssigneeIds.Distinct())
        {
            await notifications.AddAsync(new Notification
            {
                UserId = assigneeId,
                StudioId = notification.StudioId,
                Type = NotificationType.TaskAssigned,
                Status = NotificationStatus.Unread,
                Message = $"You were assigned to {notification.Kind.ToString().ToLowerInvariant()}: {notification.Title}.",
                LinkUrl = $"/teams/{notification.StudioId}/tasks",
            }, cancellationToken);
        }

        await notifications.SaveChangesAsync(cancellationToken);
        foreach (var userId in notification.AssigneeIds.Distinct())
        {
            await invalidator.InvalidateAsync(userId);
        }
    }
}

internal sealed class TaskReviewStatusChangedHandler(
    INotificationsRepository notifications,
    NotificationCacheInvalidator invalidator)
    : INotificationHandler<TaskReviewStatusChangedEvent>
{
    public async Task Handle(TaskReviewStatusChangedEvent notification, CancellationToken cancellationToken)
    {
        foreach (var reviewerId in notification.ReviewerIds.Distinct())
        {
            await notifications.AddAsync(new Notification
            {
                UserId = reviewerId,
                StudioId = notification.StudioId,
                Type = NotificationType.ReviewStatusChanged,
                Status = NotificationStatus.Unread,
                Message = $"Review {notification.Title} is now {StatusLabel(notification.Status)}.",
                LinkUrl = $"/teams/{notification.StudioId}/tasks",
            }, cancellationToken);
        }

        await notifications.SaveChangesAsync(cancellationToken);
        foreach (var userId in notification.ReviewerIds.Distinct())
        {
            await invalidator.InvalidateAsync(userId);
        }
    }

    private static string StatusLabel(TaskIssueStatus status) =>
        status switch
        {
            TaskIssueStatus.ChangesRequested => "changes requested",
            TaskIssueStatus.InProgress => "in progress",
            TaskIssueStatus.InReview => "in review",
            _ => status.ToString().ToLowerInvariant()
        };
}

internal sealed class TaskCommentMentionedHandler(
    INotificationsRepository notifications,
    NotificationCacheInvalidator invalidator)
    : INotificationHandler<TaskCommentMentionedEvent>
{
    public async Task Handle(TaskCommentMentionedEvent notification, CancellationToken cancellationToken)
    {
        foreach (var userId in notification.MentionedUserIds.Distinct())
        {
            await notifications.AddAsync(new Notification
            {
                UserId = userId,
                StudioId = notification.StudioId,
                Type = NotificationType.TaskCommentMentioned,
                Status = NotificationStatus.Unread,
                Message = $"{notification.AuthorDisplayName} mentioned you in {notification.Title}.",
                LinkUrl = $"/teams/{notification.StudioId}/tasks?taskId={notification.TaskIssueId}",
            }, cancellationToken);
        }

        await notifications.SaveChangesAsync(cancellationToken);
        foreach (var userId in notification.MentionedUserIds.Distinct())
        {
            await invalidator.InvalidateAsync(userId);
        }
    }
}
