using Kuvox.Api.Modules.Shared.Contracts;

namespace Kuvox.Api.Modules.Media.Contracts;

public sealed record MediaProjectionChangedEvent(Guid MediaId) : IIntegrationEvent;
