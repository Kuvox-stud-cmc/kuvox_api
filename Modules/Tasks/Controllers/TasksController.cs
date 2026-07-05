using Kuvox.Api.Modules.Shared.Infrastructure;
using Kuvox.Api.Modules.Tasks.Contracts;
using Kuvox.Api.Modules.Tasks.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kuvox.Api.Modules.Tasks.Controllers;

[Authorize]
[ApiController]
[Route("api/tasks")]
[Produces("application/json")]
public sealed class TasksController(IServiceProvider services) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<TaskIssueDto>> List(
        [FromQuery] Guid studioId,
        [FromQuery] TaskIssueKind? kind,
        [FromQuery] TaskIssueStatus? status,
        [FromQuery] Guid? assigneeId,
        [FromQuery] Guid? milestoneId,
        [FromQuery] Guid? labelId,
        [FromQuery] Guid? projectId,
        [FromQuery] DateTimeOffset? dueBefore,
        CancellationToken ct) =>
        Tasks.ListAsync(studioId, Caller(), new TaskIssueFilters(kind, status, assigneeId, milestoneId, labelId, projectId, dueBefore), ct);

    [HttpGet("assigned-to-me")]
    public Task<IReadOnlyList<TaskIssueDto>> AssignedToMe(
        [FromQuery] Guid? studioId,
        [FromQuery] TaskIssueKind? kind,
        [FromQuery] TaskIssueStatus? status,
        [FromQuery] Guid? milestoneId,
        [FromQuery] Guid? labelId,
        [FromQuery] Guid? projectId,
        [FromQuery] DateTimeOffset? dueBefore,
        CancellationToken ct) =>
        Tasks.ListAssignedToMeAsync(Caller(), new TaskIssueFilters(kind, status, null, milestoneId, labelId, projectId, dueBefore), studioId, ct);

    [HttpPost]
    public Task<TaskIssueDto> Create([FromQuery] Guid studioId, CreateTaskIssueRequest request, CancellationToken ct) =>
        Tasks.CreateAsync(studioId, Caller(), request, ct);

    [HttpGet("{id:guid}")]
    public Task<TaskIssueDetailDto> Get(Guid id, CancellationToken ct) =>
        Tasks.GetAsync(id, Caller(), ct);

    [HttpPut("{id:guid}")]
    public Task<TaskIssueDto> Update(Guid id, UpdateTaskIssueRequest request, CancellationToken ct) =>
        Tasks.UpdateAsync(id, Caller(), request, ct);

    [HttpPut("{id:guid}/status")]
    public Task<TaskIssueDto> UpdateStatus(Guid id, UpdateTaskIssueStatusRequest request, CancellationToken ct) =>
        Tasks.UpdateStatusAsync(id, Caller(), request, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await Tasks.DeleteAsync(id, Caller(), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/comments")]
    public Task<TaskCommentDto> CreateComment(Guid id, CreateTaskCommentRequest request, CancellationToken ct) =>
        Tasks.CreateCommentAsync(id, Caller(), request, ct);

    [HttpPut("comments/{commentId:guid}")]
    public Task<TaskCommentDto> UpdateComment(Guid commentId, UpdateTaskCommentRequest request, CancellationToken ct) =>
        Tasks.UpdateCommentAsync(commentId, Caller(), request, ct);

    [HttpDelete("comments/{commentId:guid}")]
    public async Task<IActionResult> DeleteComment(Guid commentId, CancellationToken ct)
    {
        await Tasks.DeleteCommentAsync(commentId, Caller(), ct);
        return NoContent();
    }

    [HttpGet("milestones")]
    public Task<IReadOnlyList<TaskMilestoneDto>> ListMilestones([FromQuery] Guid studioId, CancellationToken ct) =>
        Tasks.ListMilestonesAsync(studioId, Caller(), ct);

    [HttpPost("milestones")]
    public Task<TaskMilestoneDto> CreateMilestone([FromQuery] Guid studioId, CreateTaskMilestoneRequest request, CancellationToken ct) =>
        Tasks.CreateMilestoneAsync(studioId, Caller(), request, ct);

    [HttpPut("milestones/{id:guid}")]
    public Task<TaskMilestoneDto> UpdateMilestone(Guid id, UpdateTaskMilestoneRequest request, CancellationToken ct) =>
        Tasks.UpdateMilestoneAsync(id, Caller(), request, ct);

    [HttpDelete("milestones/{id:guid}")]
    public async Task<IActionResult> DeleteMilestone(Guid id, CancellationToken ct)
    {
        await Tasks.DeleteMilestoneAsync(id, Caller(), ct);
        return NoContent();
    }

    [HttpGet("labels")]
    public Task<IReadOnlyList<TaskLabelDto>> ListLabels([FromQuery] Guid studioId, CancellationToken ct) =>
        Tasks.ListLabelsAsync(studioId, Caller(), ct);

    [HttpPost("labels")]
    public Task<TaskLabelDto> CreateLabel([FromQuery] Guid studioId, CreateTaskLabelRequest request, CancellationToken ct) =>
        Tasks.CreateLabelAsync(studioId, Caller(), request, ct);

    [HttpPut("labels/{id:guid}")]
    public Task<TaskLabelDto> UpdateLabel(Guid id, UpdateTaskLabelRequest request, CancellationToken ct) =>
        Tasks.UpdateLabelAsync(id, Caller(), request, ct);

    [HttpDelete("labels/{id:guid}")]
    public async Task<IActionResult> DeleteLabel(Guid id, CancellationToken ct)
    {
        await Tasks.DeleteLabelAsync(id, Caller(), ct);
        return NoContent();
    }

    private CallerContext Caller() =>
        User.ToCallerContext() ?? throw DomainException.Forbidden("Invalid token.");

    private ITaskService Tasks => services.GetRequiredService<ITaskService>();
}
