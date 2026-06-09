using Kuvox.Api.Modules.Shared.Contracts;

namespace Kuvox.Api.Modules.Projects.Contracts;

/// <summary>
/// Raised when a project is deleted. Downstream modules (Timelines, Media) subscribe via
/// <c>INotificationHandler&lt;ProjectDeletedEvent&gt;</c> to clean up their own data (Rule 4).
/// </summary>
public sealed record ProjectDeletedEvent(Guid ProjectId) : IIntegrationEvent;
