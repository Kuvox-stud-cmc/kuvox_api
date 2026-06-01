using Kuvox.Api.Modules.Shared.Contracts;

namespace Kuvox.Api.Modules.Auth.Contracts;

/// <summary>
/// Raised when a new user completes registration. Other modules may subscribe via
/// <c>INotificationHandler&lt;UserRegisteredEvent&gt;</c> (Rule 4).
/// </summary>
public sealed record UserRegisteredEvent(Guid UserId, string Email) : IIntegrationEvent;
