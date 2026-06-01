namespace Kuvox.Api.Modules.Videos.Contracts;

/// <summary>Shareable video projection for other modules (Rule 2).</summary>
public sealed record VideoSummary(Guid Id, Guid ProjectId, string Filename, double DurationSeconds, string Status);
