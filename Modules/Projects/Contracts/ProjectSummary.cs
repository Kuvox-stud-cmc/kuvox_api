namespace Kuvox.Api.Modules.Projects.Contracts;

/// <summary>Minimal project kind exposed through the Projects public contract.</summary>
public enum ProjectContentKind
{
    Video,
    Image
}

/// <summary>Shareable project projection for other modules (Rule 2).</summary>
public sealed record ProjectSummary(Guid Id, Guid OwnerId, ProjectOwnerKind OwnerKind, ProjectContentKind Kind, string Name, string Status);

/// <summary>Authorized project projection for cross-module document storage.</summary>
public sealed record ProjectDocumentAccess(Guid Id, ProjectContentKind Kind, string Name, DateTimeOffset UpdatedAt);
