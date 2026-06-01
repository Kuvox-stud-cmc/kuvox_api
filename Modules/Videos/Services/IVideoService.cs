using Kuvox.Api.Modules.Videos.Dtos;

namespace Kuvox.Api.Modules.Videos.Services;

/// <summary>
/// Module-internal business API of the Videos module (scaffolded, not yet implemented).
/// Public only for the public controller's DI; impl stays <c>internal</c> (Rule 1). The
/// cross-module surface is <c>Videos.Contracts</c> (Rule 2).
/// </summary>
public interface IVideoService
{
    Task<IReadOnlyList<VideoDto>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<VideoDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Registers an uploaded object and dispatches an ingestion job to the AI service.</summary>
    Task<VideoDto> RegisterAsync(RegisterVideoRequest request, CancellationToken cancellationToken = default);
}
