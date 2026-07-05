using Kuvox.Api.Modules.Shared.Contracts;

namespace Kuvox.Api.Modules.Tasks.Contracts;

public sealed record TaskAssignedEvent(
    Guid TaskIssueId,
    Guid StudioId,
    TaskIssueKind Kind,
    string Title,
    IReadOnlyCollection<Guid> AssigneeIds) : IIntegrationEvent;
