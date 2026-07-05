using Kuvox.Api.Modules.Shared.Contracts;

namespace Kuvox.Api.Modules.Tasks.Contracts;

public sealed record TaskReviewStatusChangedEvent(
    Guid TaskIssueId,
    Guid StudioId,
    string Title,
    TaskIssueStatus Status,
    IReadOnlyCollection<Guid> ReviewerIds) : IIntegrationEvent;
