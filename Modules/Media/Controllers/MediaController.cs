using Kuvox.Api.Modules.Media.Dtos;
using Kuvox.Api.Modules.Media.Services;
using Kuvox.Api.Modules.Shared.Dtos;
using Kuvox.Api.Modules.Shared.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kuvox.Api.Modules.Media.Controllers;

/// <summary>
/// Real Media endpoints (Phase 2). Symmetric with <c>ProjectsController</c>: the active
/// workspace comes from the JWT — Personal by default, or a Team studio via <c>?studioId=...</c>
/// when the caller holds the matching membership claim.
/// </summary>
[Authorize]
[ApiController]
[Route("api/media")]
[Produces("application/json")]
public sealed class MediaController(IMediaService media) : ControllerBase
{
    /// <summary>List the current workspace's media library.</summary>
    [HttpGet]
    public Task<PagedResult<MediaDto>> List(
        [FromQuery] Guid? studioId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        media.ListByWorkspaceAsync(ResolveWorkspace(studioId), Caller(), page, pageSize, ct);

    /// <summary>Media shared with the caller by other owners.</summary>
    [HttpGet("shared")]
    public Task<PagedResult<MediaDto>> Shared(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        media.ListSharedWithMeAsync(Caller().UserId, page, pageSize, ct);

    /// <summary>The current workspace's Trash (soft-deleted media).</summary>
    [HttpGet("trash")]
    public Task<PagedResult<MediaTrashItemDto>> Trash(
        [FromQuery] Guid? studioId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        media.ListTrashAsync(ResolveWorkspace(studioId), page, pageSize, ct);

    [HttpGet("{id:guid}")]
    public Task<MediaDto> Get(Guid id, CancellationToken ct) => media.GetAsync(id, Caller(), ct);

    /// <summary>Streams a stored media object variant for the current caller.</summary>
    [HttpGet("{id:guid}/object/{variant}")]
    [HttpHead("{id:guid}/object/{variant}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetObject(Guid id, string variant, CancellationToken ct)
    {
        var file = await media.GetObjectAsync(id, variant, Caller(), ct);
        Response.Headers.CacheControl = "private, max-age=300";
        if (file.ContentLength is > 0)
        {
            Response.ContentLength = file.ContentLength;
        }

        if (!string.IsNullOrWhiteSpace(file.ETag))
        {
            Response.Headers.ETag = file.ETag;
        }

        return File(file.Stream, file.ContentType, enableRangeProcessing: true);
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    public Task<MediaDto> Upload(
        [FromQuery] Guid? studioId, 
        [FromForm] UploadMediaRequest request, 
        CancellationToken ct) =>
        media.UploadRawAsync(ResolveWorkspace(studioId), Caller(), request, ct);

    [HttpPut("{id:guid}/favorite")]
    public Task<MediaDto> Favorite(Guid id, ToggleMediaFavoriteRequest request, CancellationToken ct) =>
        media.SetFavoriteAsync(id, Caller(), request, ct);

    [HttpPost("{id:guid}/share")]
    public async Task<IActionResult> Share(Guid id, ShareMediaRequest request, CancellationToken ct)
    {
        await media.ShareAsync(id, Caller(), request, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}/share/{userId:guid}")]
    public async Task<IActionResult> Unshare(Guid id, Guid userId, CancellationToken ct)
    {
        await media.UnshareAsync(id, Caller(), userId, ct);
        return NoContent();
    }

    /// <summary>Move a media item to Trash (soft-delete).</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await media.SoftDeleteAsync(id, Caller(), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id, CancellationToken ct)
    {
        await media.RestoreAsync(id, Caller(), ct);
        return NoContent();
    }

    /// <summary>Permanently delete a media item (hard-delete + <c>MediaDeletedEvent</c>).</summary>
    [HttpDelete("{id:guid}/permanent")]
    public async Task<IActionResult> PermanentDelete(Guid id, CancellationToken ct)
    {
        await media.PermanentDeleteAsync(id, Caller(), ct);
        return NoContent();
    }

    private CallerContext Caller() =>
        User.ToCallerContext() ?? throw DomainException.Forbidden("Invalid token.");

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
