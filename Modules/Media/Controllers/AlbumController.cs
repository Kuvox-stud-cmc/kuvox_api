using Kuvox.Api.Modules.Media.Dtos;
using Kuvox.Api.Modules.Shared.Dtos;
using Kuvox.Api.Modules.Media.Services;
using Kuvox.Api.Modules.Shared.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kuvox.Api.Modules.Media.Controllers;

[ApiController]
[Route("api/albums")]
[Authorize]
public sealed class AlbumController(IAlbumService albumService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<AlbumDto>> CreateAlbum(
        [FromQuery] Guid? studioId,
        [FromBody] CreateAlbumDto request,
        CancellationToken cancellationToken)
    {
        var album = await albumService.CreateAlbumAsync(ResolveWorkspace(studioId), Caller(), request, cancellationToken);
        return CreatedAtAction(nameof(ListAlbums), new { id = album.Id }, album);
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AlbumDto>>> ListAlbums(
        [FromQuery] Guid? studioId,
        [FromQuery] bool includeSystem = false,
        CancellationToken cancellationToken = default)
    {
        var albums = await albumService.ListAlbumsAsync(ResolveWorkspace(studioId), Caller(), includeSystem, cancellationToken);
        return Ok(albums);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAlbum(
        Guid id,
        [FromQuery] Guid? studioId,
        CancellationToken cancellationToken)
    {
        await albumService.DeleteAlbumAsync(ResolveWorkspaceOrNull(studioId), id, Caller(), cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/favorite")]
    public async Task<ActionResult<AlbumDto>> FavoriteAlbum(
        Guid id,
        [FromBody] ToggleAlbumFavoriteRequest request,
        CancellationToken cancellationToken)
    {
        var album = await albumService.SetFavoriteAsync(id, Caller(), request, cancellationToken);
        return Ok(album);
    }

    [HttpGet("{id:guid}/media")]
    public async Task<ActionResult<PagedResult<MediaDto>>> ListAlbumMedia(
        Guid id,
        [FromQuery] Guid? studioId,
        [FromQuery] bool includeSystem = false,
        CancellationToken cancellationToken = default)
    {
        var media = await albumService.ListAlbumMediaAsync(ResolveWorkspaceOrNull(studioId), id, Caller(), includeSystem, cancellationToken);
        return Ok(media);
    }

    [HttpPost("{id:guid}/media")]
    public async Task<IActionResult> AddMediaToAlbum(
        Guid id,
        [FromQuery] Guid? studioId,
        [FromBody] AddMediaToAlbumDto request,
        CancellationToken cancellationToken)
    {
        await albumService.AddMediaToAlbumAsync(ResolveWorkspaceOrNull(studioId), id, request.MediaIds, Caller(), cancellationToken);
        return NoContent();
    }

    [HttpPost("audio-categories/{category}/media")]
    public async Task<IActionResult> AssignAudioCategory(
        string category,
        [FromQuery] Guid? studioId,
        [FromBody] AssignAudioCategoryDto request,
        CancellationToken cancellationToken)
    {
        await albumService.AssignAudioCategoryAsync(ResolveWorkspace(studioId), Caller(), category, request.MediaIds, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}/media")]
    public async Task<IActionResult> RemoveMediaFromAlbum(
        Guid id,
        [FromQuery] Guid? studioId,
        [FromBody] DeleteMediaFromAlbumDto request,
        CancellationToken cancellationToken)
    {
        await albumService.DeleteMediaFromAlbumAsync(ResolveWorkspaceOrNull(studioId), id, request.MediaIds, Caller(), cancellationToken);
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

    private WorkspaceScope? ResolveWorkspaceOrNull(Guid? studioId) =>
        studioId is { } ? ResolveWorkspace(studioId) : null;
}
