using Kuvox.Api.Modules.Auth.Contracts;
using Kuvox.Api.Modules.Media.Contracts;
using Kuvox.Api.Modules.Media.Dtos;
using Kuvox.Api.Modules.Media.Enums;
using Kuvox.Api.Modules.Media.Models;
using Kuvox.Api.Modules.Media.Repositories;
using Kuvox.Api.Modules.Shared.Dtos;
using Kuvox.Api.Modules.Shared.Infrastructure;
using MediatR;

namespace Kuvox.Api.Modules.Media.Services;

/// <summary>
/// Real Media business logic: workspace-scoped listing, "shared with me", sharing,
/// soft-delete → trash → restore → permanent delete. Mirrors <c>ProjectService</c>. Resolves
/// invitees through the Auth public contract (<see cref="IAuthApi"/>, Rule 2) and publishes
/// <see cref="MediaDeletedEvent"/> on permanent delete (Rule 4).
/// </summary>
internal sealed class MediaService(IMediaRepository media, IAuthApi auth, IMediator mediator)
    : IMediaService
{
    /// <summary>Trash auto-purge window (kept in sync with <c>TrashPurgeService</c>).</summary>
    public static readonly TimeSpan TrashRetention = TimeSpan.FromDays(7);

    public async Task<PagedResult<MediaDto>> ListByWorkspaceAsync(
        WorkspaceScope scope, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Normalize(page, pageSize);
        var (items, total) = await media.ListByWorkspaceAsync(OwnerKindOf(scope), scope.OwnerId, page, pageSize, cancellationToken);
        return new PagedResult<MediaDto>(items.Select(ToDto).ToList(), page, pageSize, total);
    }

    public async Task<PagedResult<MediaDto>> ListSharedWithMeAsync(
        Guid userId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Normalize(page, pageSize);
        var (items, total) = await media.ListSharedWithUserAsync(userId, page, pageSize, cancellationToken);
        return new PagedResult<MediaDto>(items.Select(x => ToDto(x.Media)).ToList(), page, pageSize, total);
    }

    public async Task<PagedResult<MediaTrashItemDto>> ListTrashAsync(
        WorkspaceScope scope, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Normalize(page, pageSize);
        var (items, total) = await media.ListTrashAsync(OwnerKindOf(scope), scope.OwnerId, page, pageSize, cancellationToken);
        return new PagedResult<MediaTrashItemDto>(items.Select(ToTrashDto).ToList(), page, pageSize, total);
    }

    public async Task<MediaDto> GetAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var item = await LoadLiveAsync(id, cancellationToken);
        if (!await CanAccessAsync(item, caller, cancellationToken))
        {
            throw DomainException.Forbidden("You do not have access to this media item.");
        }

        return ToDto(item);
    }

    public async Task<MediaDto> RegisterAsync(
        WorkspaceScope scope, CallerContext caller, RegisterMediaRequest request, CancellationToken cancellationToken = default)
    {
        if (!scope.IsStudio && !await auth.UserExistsAsync(scope.OwnerId, cancellationToken))
        {
            throw DomainException.BadRequest("Unknown owner.");
        }

        var item = new Models.Media
        {
            OwnerId = scope.OwnerId,
            OwnerKind = OwnerKindOf(scope),
            Kind = request.Kind,
            ProjectId = request.ProjectId,
            Filename = request.Filename.Trim(),
            StorageKey = request.StorageKey.Trim(),
            SizeBytes = request.SizeBytes,
        };

        await media.AddAsync(item, cancellationToken);
        await media.SaveChangesAsync(cancellationToken);

        // NOTE: ingestion job dispatch (RabbitMQ/gRPC) is out of scope for Phase 2 — record only.
        return ToDto(item);
    }

    public async Task ShareAsync(
        Guid id, CallerContext caller, ShareMediaRequest request, CancellationToken cancellationToken = default)
    {
        var item = await LoadLiveAsync(id, cancellationToken);
        RequireManage(item, caller);

        var invitee = await auth.GetSummaryByEmailAsync(request.Email.Trim().ToLowerInvariant(), cancellationToken)
            ?? throw DomainException.NotFound("No user with that email.");

        if (item.OwnerKind == OwnerKind.User && invitee.Id == item.OwnerId)
        {
            throw DomainException.Conflict("The owner already has access.");
        }

        var existing = await media.GetMediaUserAsync(item.Id, invitee.Id, cancellationToken);
        if (existing is null)
        {
            await media.AddMediaUserAsync(
                new MediaUser { MediaId = item.Id, UserId = invitee.Id, Role = request.Role },
                cancellationToken);
        }
        else
        {
            existing.Role = request.Role;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await media.SaveChangesAsync(cancellationToken);
    }

    public async Task UnshareAsync(Guid id, CallerContext caller, Guid userId, CancellationToken cancellationToken = default)
    {
        var item = await LoadLiveAsync(id, cancellationToken);
        RequireManage(item, caller);

        var share = await media.GetMediaUserAsync(item.Id, userId, cancellationToken);
        if (share is not null)
        {
            media.RemoveMediaUser(share);
            await media.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task SoftDeleteAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var item = await LoadLiveAsync(id, cancellationToken);
        RequireManage(item, caller);

        item.DeletedAt = DateTimeOffset.UtcNow;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await media.SaveChangesAsync(cancellationToken);
    }

    public async Task RestoreAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var item = await media.GetByIdAsync(id, cancellationToken)
            ?? throw DomainException.NotFound("Media not found.");
        RequireManage(item, caller);

        item.DeletedAt = null;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await media.SaveChangesAsync(cancellationToken);
    }

    public async Task PermanentDeleteAsync(Guid id, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var item = await media.GetByIdAsync(id, cancellationToken)
            ?? throw DomainException.NotFound("Media not found.");
        RequireManage(item, caller);

        media.Remove(item);
        await media.SaveChangesAsync(cancellationToken);

        await mediator.Publish(new MediaDeletedEvent(item.Id), cancellationToken);
    }

    private async Task<Models.Media> LoadLiveAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await media.GetByIdAsync(id, cancellationToken);
        return item is null || item.DeletedAt is not null
            ? throw DomainException.NotFound("Media not found.")
            : item;
    }

    private static bool CanManage(Models.Media item, CallerContext caller) =>
        item.OwnerKind == OwnerKind.User
            ? caller.OwnsAsUser(item.OwnerId)
            : caller.InStudio(item.OwnerId);

    private static void RequireManage(Models.Media item, CallerContext caller)
    {
        if (!CanManage(item, caller))
        {
            throw DomainException.Forbidden("You do not have permission to modify this media item.");
        }
    }

    private async Task<bool> CanAccessAsync(Models.Media item, CallerContext caller, CancellationToken cancellationToken)
    {
        if (CanManage(item, caller))
        {
            return true;
        }

        return await media.GetMediaUserAsync(item.Id, caller.UserId, cancellationToken) is not null;
    }

    private static OwnerKind OwnerKindOf(WorkspaceScope scope) => scope.IsStudio ? OwnerKind.Studio : OwnerKind.User;

    private static (int Page, int PageSize) Normalize(int page, int pageSize) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, 100));

    private static MediaDto ToDto(Models.Media m) =>
        new(m.Id, m.OwnerId, m.OwnerKind, m.Kind, m.ProjectId, m.Filename, m.StorageKey, m.SizeBytes,
            m.Status, m.DurationSeconds, m.Width, m.Height, m.Codec, m.CreatedAt);

    private static MediaTrashItemDto ToTrashDto(Models.Media m)
    {
        var deletedAt = m.DeletedAt ?? DateTimeOffset.UtcNow;
        var remaining = (deletedAt + TrashRetention) - DateTimeOffset.UtcNow;
        var purgesInDays = Math.Max(0, (int)Math.Ceiling(remaining.TotalDays));
        return new MediaTrashItemDto(m.Id, m.Kind, m.Filename, deletedAt, purgesInDays);
    }
}
