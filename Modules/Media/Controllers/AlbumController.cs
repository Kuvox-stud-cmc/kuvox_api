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
        [FromBody] CreateAlbumDto request,
        CancellationToken cancellationToken)
    {
        var userId = Caller().UserId;
        var album = await albumService.CreateAlbumAsync(request, userId, cancellationToken);
        return CreatedAtAction(nameof(ListAlbums), new { id = album.Id }, album);
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AlbumDto>>> ListAlbums(CancellationToken cancellationToken)
    {
        var userId = Caller().UserId;
        var albums = await albumService.ListAlbumsAsync(userId, cancellationToken);
        return Ok(albums);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAlbum(Guid id, CancellationToken cancellationToken)
    {
        var userId = Caller().UserId;
        await albumService.DeleteAlbumAsync(id, userId, cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/media")]
    public async Task<ActionResult<PagedResult<MediaDto>>> ListAlbumMedia(Guid id, CancellationToken cancellationToken)
    {
        var userId = Caller().UserId;
        var media = await albumService.ListAlbumMediaAsync(id, userId, cancellationToken);
        return Ok(media);
    }

    [HttpPost("{id:guid}/media")]
    public async Task<IActionResult> AddMediaToAlbum(
        Guid id,
        [FromBody] AddMediaToAlbumDto request,
        CancellationToken cancellationToken)
    {
        var userId = Caller().UserId;
        await albumService.AddMediaToAlbumAsync(id, request.MediaIds, userId, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}/media")]
    public async Task<IActionResult> RemoveMediaFromAlbum(
        Guid id,
        [FromBody] DeleteMediaFromAlbumDto request,
        CancellationToken cancellationToken)
    {
        var userId = Caller().UserId;
        await albumService.DeleteMediaFromAlbumAsync(id, request.MediaIds, userId, cancellationToken);
        return NoContent();
    }

    private CallerContext Caller() =>
        User.ToCallerContext() ?? throw DomainException.Forbidden("Invalid token.");
}
