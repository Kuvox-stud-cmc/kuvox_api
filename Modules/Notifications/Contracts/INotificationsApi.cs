namespace Kuvox.Api.Modules.Notifications;

public interface INotificationsApi
{
    Task CreateAsync(
        Guid userId,
        Guid? studioId,
        string type,
        string message,
        string? linkUrl = null,
        CancellationToken cancellationToken = default);
}
