using Kuvox.Api.Modules.Notifications.Enums;
using Kuvox.Api.Modules.Notifications.Models;
using Kuvox.Api.Modules.Notifications.Repositories;
using Kuvox.Api.Modules.Tasks.Contracts;
using MediatR;

namespace Kuvox.Api.Modules.Notifications.Services;

internal sealed class TaskAssignedHandler(INotificationsRepository notifications)
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
    }
}

internal sealed class TaskReviewStatusChangedHandler(INotificationsRepository notifications)
    : INotificationHandler<TaskReviewStatusChangedEvent>
{
    public async Task Handle(TaskReviewStatusChangedEvent notification, CancellationToken cancellationToken)
    {
        foreach (var assigneeId in notification.AssigneeIds.Distinct())
        {
            await notifications.AddAsync(new Notification
            {
                UserId = assigneeId,
                StudioId = notification.StudioId,
                Type = NotificationType.ReviewStatusChanged,
                Status = NotificationStatus.Unread,
                Message = $"Review {notification.Title} is now {StatusLabel(notification.Status)}.",
                LinkUrl = $"/teams/{notification.StudioId}/tasks",
            }, cancellationToken);
        }

        await notifications.SaveChangesAsync(cancellationToken);
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
