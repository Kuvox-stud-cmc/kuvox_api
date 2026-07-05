namespace Kuvox.Api.Modules.Media.Contracts;

/// <summary>Storage and item usage projection for other modules (Rule 2).</summary>
public sealed record MediaWorkspaceUsageSummary(
    int MediaCount,
    long StorageBytesUsed
);
