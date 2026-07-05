using Kuvox.Api.Modules.Shared.Infrastructure;
using Kuvox.Api.Modules.Tasks.Contracts;

namespace Kuvox.Api.Modules.Tasks.Services;

internal interface ITaskService
{
    Task<IReadOnlyList<TaskIssueDto>> ListAsync(Guid studioId, CallerContext caller, TaskIssueFilters filters, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskIssueDto>> ListAssignedToMeAsync(CallerContext caller, TaskIssueFilters filters, Guid? studioId, CancellationToken cancellationToken = default);

    Task<TaskIssueDetailDto> GetAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default);

    Task<TaskIssueDto> CreateAsync(Guid studioId, CallerContext caller, CreateTaskIssueRequest request, CancellationToken cancellationToken = default);

    Task<TaskIssueDto> UpdateAsync(Guid id, CallerContext caller, UpdateTaskIssueRequest request, CancellationToken cancellationToken = default);

    Task<TaskIssueDto> UpdateStatusAsync(Guid id, CallerContext caller, UpdateTaskIssueStatusRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskMilestoneDto>> ListMilestonesAsync(Guid studioId, CallerContext caller, CancellationToken cancellationToken = default);

    Task<TaskMilestoneDto> CreateMilestoneAsync(Guid studioId, CallerContext caller, CreateTaskMilestoneRequest request, CancellationToken cancellationToken = default);

    Task<TaskMilestoneDto> UpdateMilestoneAsync(Guid id, CallerContext caller, UpdateTaskMilestoneRequest request, CancellationToken cancellationToken = default);

    Task DeleteMilestoneAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskLabelDto>> ListLabelsAsync(Guid studioId, CallerContext caller, CancellationToken cancellationToken = default);

    Task<TaskLabelDto> CreateLabelAsync(Guid studioId, CallerContext caller, CreateTaskLabelRequest request, CancellationToken cancellationToken = default);

    Task<TaskLabelDto> UpdateLabelAsync(Guid id, CallerContext caller, UpdateTaskLabelRequest request, CancellationToken cancellationToken = default);

    Task DeleteLabelAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default);

    Task<TaskCommentDto> CreateCommentAsync(Guid taskIssueId, CallerContext caller, CreateTaskCommentRequest request, CancellationToken cancellationToken = default);

    Task<TaskCommentDto> UpdateCommentAsync(Guid commentId, CallerContext caller, UpdateTaskCommentRequest request, CancellationToken cancellationToken = default);

    Task DeleteCommentAsync(Guid commentId, CallerContext caller, CancellationToken cancellationToken = default);
}
