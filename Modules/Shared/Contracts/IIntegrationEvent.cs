using MediatR;

namespace Kuvox.Api.Modules.Shared.Contracts;

/// <summary>
/// Marker for in-process cross-module domain events (Rule 4). Events are published via
/// MediatR's <see cref="IMediator"/> and consumed by <c>INotificationHandler&lt;T&gt;</c>
/// implementations living inside the subscribing modules. A module may reference another
/// module's event type (it lives in that module's <c>Contracts</c> namespace) but never
/// its implementation classes (Rule 1).
/// </summary>
public interface IIntegrationEvent : INotification;
