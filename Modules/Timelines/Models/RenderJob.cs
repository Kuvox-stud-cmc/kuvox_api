using Kuvox.Api.Modules.Shared.Models;

namespace Kuvox.Api.Modules.Timelines.Models;

/// <summary>
/// A render job dispatched (via RabbitMQ) to the AI service for a timeline revision. Owned
/// by the Timelines module (table <c>timelines.render_jobs</c>).
/// </summary>
public sealed class RenderJob : BaseEntity
{
    public required Guid TimelineId { get; set; }

    public Guid? RevisionId { get; set; }

    /// <summary>queued | rendering | completed | failed.</summary>
    public string Status { get; set; } = "queued";

    /// <summary>Object-storage key of the rendered output once completed.</summary>
    public string? OutputStorageKey { get; set; }
}
