using Kuvox.Api.Modules.Projects.Dtos;
using Kuvox.Api.Modules.Projects.Services;
using Kuvox.Api.Modules.Shared.Dtos;
using Kuvox.Api.Modules.Shared.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kuvox.Api.Modules.Projects.Controllers;

/// <summary>
/// Real Projects endpoints (Phase 2). The active workspace is resolved from the JWT:
/// Personal by default, or a Team studio when <c>?studioId=...</c> is supplied and the caller
/// holds the matching <c>studio</c> membership claim. The legacy <c>ownerId</c> query param is
/// gone — ownership comes from the token, never the client (plan §3).
/// </summary>
[Authorize]
[ApiController]
[Route("api/projects")]
[Produces("application/json")]
public sealed class ProjectsController(IProjectService projects) : ControllerBase
{
    /// <summary>List the current workspace's projects (Personal, or a team via <c>studioId</c>).</summary>
    [HttpGet]
    public Task<PagedResult<ProjectDto>> List(
        [FromQuery] Guid? studioId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        projects.ListByWorkspaceAsync(ResolveWorkspace(studioId), page, pageSize, ct);

    /// <summary>Projects shared with the caller by other owners.</summary>
    [HttpGet("shared")]
    public Task<PagedResult<ProjectDto>> Shared(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        projects.ListSharedWithMeAsync(Caller().UserId, page, pageSize, ct);

    /// <summary>The current workspace's Trash (soft-deleted projects).</summary>
    [HttpGet("trash")]
    public Task<PagedResult<ProjectTrashItemDto>> Trash(
        [FromQuery] Guid? studioId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        projects.ListTrashAsync(ResolveWorkspace(studioId), page, pageSize, ct);

    [HttpGet("{id:guid}")]
    public Task<ProjectDto> Get(Guid id, CancellationToken ct) => projects.GetAsync(id, Caller(), ct);

    [HttpPost]
    public Task<ProjectDto> Create([FromQuery] Guid? studioId, CreateProjectRequest request, CancellationToken ct) =>
        projects.CreateAsync(ResolveWorkspace(studioId), Caller(), request, ct);

    [HttpPut("{id:guid}")]
    public Task<ProjectDto> Update(Guid id, UpdateProjectRequest request, CancellationToken ct) =>
        projects.UpdateAsync(id, Caller(), request, ct);

    [HttpPost("{id:guid}/share")]
    public async Task<IActionResult> Share(Guid id, ShareProjectRequest request, CancellationToken ct)
    {
        await projects.ShareAsync(id, Caller(), request, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}/share/{userId:guid}")]
    public async Task<IActionResult> Unshare(Guid id, Guid userId, CancellationToken ct)
    {
        await projects.UnshareAsync(id, Caller(), userId, ct);
        return NoContent();
    }

    /// <summary>Move a project to Trash (soft-delete).</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await projects.SoftDeleteAsync(id, Caller(), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id, CancellationToken ct)
    {
        await projects.RestoreAsync(id, Caller(), ct);
        return NoContent();
    }

    /// <summary>Permanently delete a project (hard-delete + cross-module cleanup).</summary>
    [HttpDelete("{id:guid}/permanent")]
    public async Task<IActionResult> PermanentDelete(Guid id, CancellationToken ct)
    {
        await projects.PermanentDeleteAsync(id, Caller(), ct);
        return NoContent();
    }

    private CallerContext Caller() =>
        User.ToCallerContext() ?? throw DomainException.Forbidden("Invalid token.");

    /// <summary>Personal workspace by default; a studio workspace only if the caller is a member.</summary>
    private WorkspaceScope ResolveWorkspace(Guid? studioId)
    {
        var caller = Caller();
        if (studioId is not { } sid)
        {
            return new WorkspaceScope(IsStudio: false, OwnerId: caller.UserId);
        }

        if (!caller.InStudio(sid))
        {
            throw DomainException.Forbidden("You are not a member of this studio.");
        }

        return new WorkspaceScope(IsStudio: true, OwnerId: sid);
    }
}
