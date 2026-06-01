namespace Kuvox.Api.Modules.Projects.Contracts;

/// <summary>Shareable project projection for other modules (Rule 2).</summary>
public sealed record ProjectSummary(Guid Id, Guid OwnerId, string Name, string Status);
