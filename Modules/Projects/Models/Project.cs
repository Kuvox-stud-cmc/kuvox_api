using Kuvox.Api.Modules.Projects.Enums;
using Kuvox.Api.Modules.Shared.Models;

namespace Kuvox.Api.Modules.Projects.Models;

/// <summary>An editing project. Owned by the Projects module (table <c>projects.projects</c>).</summary>
public sealed class Project : BaseEntity
{
    /// <summary>
    /// Owning workspace id (user or studio; see <see cref="OwnerKind"/>). Stored as a bare id —
    /// there is NO cross-schema FK to <c>auth.users</c>/<c>auth.studios</c> and no EF navigation
    /// across modules (Rule 1 / Rule 3). Existence is checked via <c>IAuthApi</c> when needed (Rule 2).
    /// </summary>
    public required Guid OwnerId { get; set; }

    public required OwnerKind OwnerKind { get; set; }

    public required ProjectKind Kind { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public string Status { get; set; } = "draft";

    /// <summary>Soft-delete timestamp; non-null means the project is in Trash.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
