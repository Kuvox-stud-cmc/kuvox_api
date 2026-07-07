using Kuvox.Api.Modules.Shared.Models;
using Kuvox.Api.Modules.Timelines.Enums;

namespace Kuvox.Api.Modules.Timelines.Models;

/// <summary>
/// A render job requested for a timeline revision. Owned by the Timelines module
/// (table <c>timelines.render_jobs</c>).
/// </summary>
public sealed class RenderJob : BaseEntity
{
    public required Guid TimelineId { get; set; }

    public Guid? RevisionId { get; set; }

    public required Guid RequestedByUserId { get; set; }

    public string SettingsJson { get; set; } = "{}";

    /// <summary>queued | rendering | completed | failed.</summary>
    public RenderStatus Status { get; set; } = RenderStatus.Queued;

    /// <summary>Object-storage key of the rendered output once completed.</summary>
    public string? OutputStorageKey { get; set; }
}
