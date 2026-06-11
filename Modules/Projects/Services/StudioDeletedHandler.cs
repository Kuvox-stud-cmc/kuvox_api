using Kuvox.Api.Modules.Auth.Contracts;
using Kuvox.Api.Modules.Projects.Enums;
using Kuvox.Api.Modules.Projects.Repositories;
using MediatR;
using System.Threading;
using System.Threading.Tasks;

namespace Kuvox.Api.Modules.Projects.Services;

internal sealed class StudioDeletedHandler(IProjectRepository projects) : INotificationHandler<StudioDeletedEvent>
{
    public async Task Handle(StudioDeletedEvent notification, CancellationToken cancellationToken)
    {
        var (items, _) = await projects.ListByWorkspaceAsync(OwnerKind.Studio, notification.StudioId, 1, int.MaxValue, cancellationToken);
        foreach (var item in items)
        {
            item.DeletedAt = System.DateTimeOffset.UtcNow;
            item.UpdatedAt = System.DateTimeOffset.UtcNow;
        }
        await projects.SaveChangesAsync(cancellationToken);
    }
}
