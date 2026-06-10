using Kuvox.Api.Modules.Auth.Contracts;
using Kuvox.Api.Modules.Media.Enums;
using Kuvox.Api.Modules.Media.Repositories;
using MediatR;
using System.Threading;
using System.Threading.Tasks;

namespace Kuvox.Api.Modules.Media.Services;

internal sealed class StudioDeletedHandler(IMediaRepository media) : INotificationHandler<StudioDeletedEvent>
{
    public async Task Handle(StudioDeletedEvent notification, CancellationToken cancellationToken)
    {
        var (items, _) = await media.ListByWorkspaceAsync(OwnerKind.Studio, notification.StudioId, 1, int.MaxValue, cancellationToken);
        foreach (var item in items)
        {
            media.Remove(item);
        }
        await media.SaveChangesAsync(cancellationToken);
    }
}
