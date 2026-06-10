using Kuvox.Api.Modules.Media.Repositories;
using Kuvox.Api.Modules.Projects.Contracts;
using MediatR;

namespace Kuvox.Api.Modules.Media.Services;

/// <summary>
/// Reacts to a project's *permanent* deletion by hard-deleting this module's media linked to
/// that project (Rule 4). Fired only on permanent delete / trash auto-purge — soft-delete to
/// Trash does NOT publish <see cref="ProjectDeletedEvent"/>. Subscribes through MediatR,
/// referencing only the Projects module's public Contracts namespace (Rule 1). Internal.
/// </summary>
internal sealed class ProjectDeletedHandler(IMediaRepository media, ILogger<ProjectDeletedHandler> logger)
    : INotificationHandler<ProjectDeletedEvent>
{
    public async Task Handle(ProjectDeletedEvent notification, CancellationToken cancellationToken)
    {
        var deleted = await media.DeleteByProjectAsync(notification.ProjectId, cancellationToken);
        logger.LogInformation(
            "[Media] Project {ProjectId} deleted — removed {Count} media item(s).",
            notification.ProjectId, deleted);
    }
}
