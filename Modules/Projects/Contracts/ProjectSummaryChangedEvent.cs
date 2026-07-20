using Kuvox.Api.Modules.Shared.Contracts;

namespace Kuvox.Api.Modules.Projects.Contracts;

public sealed record ProjectSummaryChangedEvent(Guid StudioId) : IIntegrationEvent;
