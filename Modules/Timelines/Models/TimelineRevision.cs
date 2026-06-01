using Kuvox.Api.Modules.Shared.Models;

namespace Kuvox.Api.Modules.Timelines.Models;

/// <summary>
/// An immutable snapshot of a timeline's edit operations. Owned by the Timelines module
/// (table <c>timelines.timeline_revisions</c>).
/// </summary>
public sealed class TimelineRevision : BaseEntity
{
    public required Guid TimelineId { get; set; }

    public int RevisionNumber { get; set; }

    /// <summary>
    /// The flat list of typed editing operations as raw JSON, persisted to a Postgres
    /// <c>jsonb</c> column. The operation schema itself is validated in the AI service.
    /// </summary>
    public string Operations { get; set; } = "[]";
}
