using Kuvox.Api.Modules.Media.Contracts;
using Kuvox.Api.Modules.Projects.Repositories;
using MediatR;

namespace Kuvox.Api.Modules.Projects.Services;

internal sealed class MediaDeletedHandler(IProjectRepository projects) : INotificationHandler<MediaDeletedEvent>
{
  public async Task Handle(MediaDeletedEvent notification, CancellationToken cancellationToken)
  {
    await projects.DeleteProjectMediaByMediaIdAsync(notification.MediaId, cancellationToken);
  }
}
