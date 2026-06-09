using Kuvox.Api.Modules.Projects.Enums;
using Kuvox.Api.Modules.Shared.Models;

namespace Kuvox.Api.Modules.Projects.Models;

/// <summary>
/// Grants a non-owner user access to a project (composite <c>projects.project_users</c>).
/// Owner access stays denormalized on <see cref="Project.OwnerId"/>; this table is the
/// "shared with me" set (Rule 1 — references users by id only, no cross-schema FK).
/// </summary>
public sealed class ProjectUser : JunctionBaseEntity
{
    public required Guid ProjectId { get; set; }

    public required Guid UserId { get; set; }

    public required ProjectRole Role { get; set; }
}
