using Kuvox.Api.Modules.Shared.Models;

namespace Kuvox.Api.Modules.Timelines.Models;

/// <summary>
/// A natural-language editing command issued against a project, with its classified intent.
/// Owned by the Timelines module (table <c>timelines.command_history</c>). Feeds autocomplete.
/// </summary>
public sealed class CommandHistory : BaseEntity
{
    public required Guid ProjectId { get; set; }

    public required Guid UserId { get; set; }

    public required string CommandText { get; set; }

    /// <summary>Classified intent label, e.g. "concrete" | "abstract" | "unknown".</summary>
    public string? Intent { get; set; }
}
