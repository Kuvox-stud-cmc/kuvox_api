using Kuvox.Api.Modules.Auth.Dtos;
using Kuvox.Api.Modules.Auth.Repositories;
using Kuvox.Api.Modules.Shared.Infrastructure.Caching;
using Microsoft.Extensions.Options;

namespace Kuvox.Api.Modules.Auth.Services;

internal sealed class StudioCachePrewarmTarget(
    IStudioRepository studios,
    BusinessCache cache,
    CacheGenerationManager generations,
    CacheKeyFactory keys,
    IOptions<CachingOptions> options) : ICachePrewarmTarget
{
    private readonly StudioCacheOptions _feature = options.Value.Studio;

    public CachePrewarmKind Kind => CachePrewarmKind.StudioSettings;

    public async Task<bool> PrewarmAsync(
        CachePrewarmRequest request,
        CancellationToken cancellationToken)
    {
        var current = await generations.GetAsync(
            "studio", $"studio-{request.StudioId:N}", cancellationToken);
        if (!string.Equals(current, request.Generation, StringComparison.Ordinal))
        {
            return true;
        }

        var studio = await studios.GetByIdAsync(request.StudioId, cancellationToken);
        if (studio is null)
        {
            return false;
        }
        var workspace = new StudioWorkspaceSettingsDto(
            studio.Id, studio.Name, studio.Description, studio.AvatarUrl, studio.PublicSlug);
        var notifications = new StudioNotificationSettingsDto(
            studio.NotifyOnInvites,
            studio.NotifyOnMembers,
            studio.NotifyOnProjects,
            studio.NotifyOnMedia);

        current = await generations.GetAsync(
            "studio", $"studio-{request.StudioId:N}", cancellationToken);
        if (!string.Equals(current, request.Generation, StringComparison.Ordinal))
        {
            return true;
        }

        var workspaceWritten = await cache.TryWriteAsync(
            "studio",
            _feature,
            BusinessCacheKey.Create(
                keys, "studio-v2", "workspace-settings", "studio", request.StudioId,
                "gen", request.Generation),
            TimeSpan.FromSeconds(_feature.SettingsTtlSeconds),
            workspace,
            cancellationToken);
        var notificationsWritten = await cache.TryWriteAsync(
            "studio",
            _feature,
            BusinessCacheKey.Create(
                keys, "studio-v2", "notification-settings", "studio", request.StudioId,
                "gen", request.Generation),
            TimeSpan.FromSeconds(_feature.SettingsTtlSeconds),
            notifications,
            cancellationToken);
        return workspaceWritten && notificationsWritten;
    }
}
