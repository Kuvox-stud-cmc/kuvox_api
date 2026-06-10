using Kuvox.Api.Modules.Shared.Models;

namespace Kuvox.Api.Modules.Timelines.Models;

/// <summary>
/// A timeline within a project. Owned by the Timelines module (table <c>timelines.timelines</c>).
/// <see cref="ProjectId"/> references the Projects module by id only.
/// </summary>
public sealed class Timeline : BaseEntity
{
    public required Guid ProjectId { get; set; }

    public required string Name { get; set; }
}
