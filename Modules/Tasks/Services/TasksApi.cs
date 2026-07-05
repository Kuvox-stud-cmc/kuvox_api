using Kuvox.Api.Modules.Tasks.Contracts;
using Kuvox.Api.Modules.Tasks.Repositories;

namespace Kuvox.Api.Modules.Tasks.Services;

internal sealed class TasksApi(ITaskRepository tasks) : ITasksApi
{
    public async Task<TaskIssueSummary?> GetSummaryAsync(Guid taskIssueId, CancellationToken cancellationToken = default)
    {
        var issue = await tasks.GetIssueAsync(taskIssueId, cancellationToken);
        return issue is null
            ? null
            : new TaskIssueSummary(issue.Id, issue.StudioId, issue.ProjectId, issue.Kind, issue.Status, issue.Title);
    }
}
