using System;
using Kuvox.Api.Modules.Shared.Contracts;

namespace Kuvox.Api.Modules.Auth.Contracts;

public record StudioDeletedEvent(Guid StudioId) : IIntegrationEvent;