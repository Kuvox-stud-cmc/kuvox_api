using MediatR;

namespace Kuvox.Api.Modules.Tasks.Contracts;

public sealed record TaskCommentMentionedEvent(
    Guid TaskIssueId,
    Guid StudioId,
    string Title,
    Guid AuthorUserId,
    string AuthorDisplayName,
    IReadOnlyCollection<Guid> MentionedUserIds) : INotification;
