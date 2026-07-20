using Kuvox.Api.Modules.Shared.Contracts;

namespace Kuvox.Api.Modules.Auth.Contracts;

public sealed record StudioMembershipChangedEvent(Guid StudioId, Guid UserId) : IIntegrationEvent;
