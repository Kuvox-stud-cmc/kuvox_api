using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Kuvox.Api.Modules.Notifications.Dtos;
using Kuvox.Api.Modules.Notifications.Services;
using Kuvox.Api.Modules.Shared.Dtos;
using Kuvox.Api.Modules.Shared.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kuvox.Api.Modules.Notifications.Controllers;

[Authorize]
[ApiController]
[Route("api/notifications")]
[Produces("application/json")]
public sealed class NotificationsController(INotificationsService notifications) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<NotificationDto>> ListMine(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default) =>
        notifications.ListMineAsync(CurrentUserId(), page, pageSize, ct);

    [HttpGet("unread-count")]
    public Task<UnreadCountDto> UnreadCount(CancellationToken ct) =>
        notifications.CountUnreadAsync(CurrentUserId(), ct);

    [HttpPost("{id:guid}/read")]
    public Task<NotificationDto> MarkRead(Guid id, CancellationToken ct) =>
        notifications.MarkReadAsync(CurrentUserId(), id, ct);

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await notifications.MarkAllReadAsync(CurrentUserId(), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/archive")]
    public Task<NotificationDto> Archive(Guid id, CancellationToken ct) =>
        notifications.ArchiveAsync(CurrentUserId(), id, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await notifications.DeleteAsync(CurrentUserId(), id, ct);
        return NoContent();
    }

    private Guid CurrentUserId()
    {
        var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(sub, out var userId))
        {
            throw DomainException.Forbidden("Invalid token.");
        }

        return userId;
    }
}
