using Kuvox.Api.Modules.Shared.Contracts;

namespace Kuvox.Api.Modules.Media.Contracts;

/// <summary>
/// Raised when a media item is permanently deleted (explicit hard-delete or trash auto-purge).
/// Other modules may subscribe via <c>INotificationHandler&lt;MediaDeletedEvent&gt;</c> to clean
/// up (e.g. drop derived data); none are required today (Rule 4).
/// </summary>
public sealed record MediaDeletedEvent(Guid MediaId) : IIntegrationEvent;
