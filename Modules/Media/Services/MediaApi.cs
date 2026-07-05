using Kuvox.Api.Modules.Media.Contracts;
using Kuvox.Api.Modules.Media.Repositories;

namespace Kuvox.Api.Modules.Media.Services;

/// <summary>Implements the public <see cref="IMediaApi"/> read facade (Rule 2). Internal (Rule 1).</summary>
internal sealed class MediaApi(IMediaRepository media) : IMediaApi
{
    public async Task<MediaSummary?> GetSummaryAsync(Guid mediaId, CancellationToken cancellationToken = default)
    {
        var item = await media.GetByIdAsync(mediaId, cancellationToken);
        return item is null
            ? null
            : new MediaSummary(item.Id, item.OwnerId, item.OwnerKind, item.Kind, item.Filename, item.Status.ToString());
    }

    public async Task<MediaWorkspaceUsageSummary> GetWorkspaceUsageAsync(
        Guid ownerId,
        Enums.OwnerKind ownerKind,
        CancellationToken cancellationToken = default)
    {
        var usage = await media.GetStorageUsageAsync(ownerKind, ownerId, cancellationToken);
        return new MediaWorkspaceUsageSummary(usage.MediaCount, usage.StorageBytesUsed);
    }
}
