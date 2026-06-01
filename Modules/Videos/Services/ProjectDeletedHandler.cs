using Kuvox.Api.Modules.Projects.Contracts;
using Kuvox.Api.Modules.Videos.Repositories;
using MediatR;

namespace Kuvox.Api.Modules.Videos.Services;

/// <summary>
/// Reacts to a project deletion by removing this module's videos for that project (Rule 4).
/// Subscribes through MediatR to the Projects module's <see cref="ProjectDeletedEvent"/> —
/// referencing only that module's public Contracts namespace (Rule 1). Internal.
/// </summary>
internal sealed class ProjectDeletedHandler(IVideoRepository videos, ILogger<ProjectDeletedHandler> logger)
    : INotificationHandler<ProjectDeletedEvent>
{
    public async Task Handle(ProjectDeletedEvent notification, CancellationToken cancellationToken)
    {
        var deleted = await videos.DeleteByProjectAsync(notification.ProjectId, cancellationToken);
        logger.LogInformation(
            "[Videos] Project {ProjectId} deleted — removed {Count} video(s).",
            notification.ProjectId, deleted);
    }
}
