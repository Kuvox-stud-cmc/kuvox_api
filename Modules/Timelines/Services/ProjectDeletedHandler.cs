using Kuvox.Api.Modules.Projects.Contracts;
using Kuvox.Api.Modules.Timelines.Repositories;
using MediatR;

namespace Kuvox.Api.Modules.Timelines.Services;

/// <summary>
/// Removes this module's timelines for a deleted project (Rule 4). Subscribes via MediatR to
/// the Projects module's <see cref="ProjectDeletedEvent"/> — referencing only its public
/// Contracts namespace (Rule 1). Internal.
/// </summary>
internal sealed class ProjectDeletedHandler(ITimelineRepository timelines, ILogger<ProjectDeletedHandler> logger)
    : INotificationHandler<ProjectDeletedEvent>
{
    public async Task Handle(ProjectDeletedEvent notification, CancellationToken cancellationToken)
    {
        var deleted = await timelines.DeleteByProjectAsync(notification.ProjectId, cancellationToken);
        logger.LogInformation(
            "[Timelines] Project {ProjectId} deleted — removed {Count} timeline(s).",
            notification.ProjectId, deleted);
    }
}
