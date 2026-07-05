namespace Kuvox.Api.Modules.Tasks.Contracts;

public sealed record TaskIssueSummary(
    Guid Id,
    Guid StudioId,
    Guid? ProjectId,
    TaskIssueKind Kind,
    TaskIssueStatus Status,
    string Title);
