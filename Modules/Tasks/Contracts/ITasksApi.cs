namespace Kuvox.Api.Modules.Tasks.Contracts;

public interface ITasksApi
{
    Task<TaskIssueSummary?> GetSummaryAsync(Guid taskIssueId, CancellationToken cancellationToken = default);
}
