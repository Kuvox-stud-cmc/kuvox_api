using Kuvox.Api.Modules.Auth.Dtos;
using Kuvox.Api.Modules.Auth.Services;
using Kuvox.Api.Modules.Shared.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kuvox.Api.Modules.Auth.Controllers;

/// <summary>
/// Studio (team) APIs: the caller's teams for the workspace switcher, studio creation, and
/// Admin-only member management. Authorization is enforced in <see cref="IStudioService"/>
/// against persisted memberships.
/// </summary>
[Authorize]
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class StudiosController(IStudioService studios) : ControllerBase
{
    /// <summary>Studios the caller belongs to (for the workspace switcher).</summary>
    [HttpGet("me/studios")]
    public Task<IReadOnlyList<StudioDto>> MyStudios(CancellationToken ct) =>
        studios.ListMineAsync(CallerId(), ct);

    /// <summary>Create a studio; the caller becomes its first Admin.</summary>
    [HttpPost("studios")]
    public Task<StudioDto> Create(CreateStudioRequest request, CancellationToken ct) =>
        studios.CreateAsync(CallerId(), request, ct);

    [HttpGet("studios/{studioId:guid}/members")]
    public Task<IReadOnlyList<StudioMemberDto>> Members(Guid studioId, CancellationToken ct) =>
        studios.ListMembersAsync(studioId, CallerId(), ct);

    [HttpPost("studios/{studioId:guid}/members")]
    public Task<StudioMemberDto> AddMember(Guid studioId, AddStudioMemberRequest request, CancellationToken ct) =>
        studios.AddMemberAsync(studioId, CallerId(), request, ct);

    [HttpPatch("studios/{studioId:guid}/members/{userId:guid}")]
    public Task<StudioMemberDto> UpdateMember(Guid studioId, Guid userId, UpdateStudioMemberRequest request, CancellationToken ct) =>
        studios.UpdateMemberAsync(studioId, CallerId(), userId, request, ct);

    [HttpDelete("studios/{studioId:guid}/members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid studioId, Guid userId, CancellationToken ct)
    {
        await studios.RemoveMemberAsync(studioId, CallerId(), userId, ct);
        return NoContent();
    }

    [HttpPatch("studios/{studioId:guid}")]
    public Task<StudioDto> RenameStudio(Guid studioId, [FromBody] RenameStudioRequest request, CancellationToken ct) =>
        studios.RenameAsync(studioId, CallerId(), request, ct);

    [HttpDelete("studios/{studioId:guid}")]
    public async Task<IActionResult> DeleteStudio(Guid studioId, CancellationToken ct)
    {
        await studios.DeleteAsync(studioId, CallerId(), ct);
        return NoContent();
    }

    private Guid CallerId() =>
        User.GetUserId() ?? throw DomainException.Forbidden("Invalid token.");
}
