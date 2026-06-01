namespace Kuvox.Api.Modules.Timelines.Contracts;

/// <summary>Shareable timeline projection for other modules (Rule 2).</summary>
public sealed record TimelineSummary(Guid Id, Guid ProjectId, string Name);
