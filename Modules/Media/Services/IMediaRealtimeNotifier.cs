using Kuvox.Api.Modules.Media.Dtos;
using MediaEntity = Kuvox.Api.Modules.Media.Models.Media;

namespace Kuvox.Api.Modules.Media.Services;

internal interface IMediaRealtimeNotifier
{
    Task MediaUpdatedAsync(
        MediaEntity media,
        MediaDto dto,
        string phase,
        CancellationToken cancellationToken = default,
        int? shotCount = null,
        string? errorCode = null,
        string? errorMessage = null);
}
