using Kuvox.Api.Modules.Shared.Models;

namespace Kuvox.Api.Modules.Tasks.Models;

internal sealed class TaskLabel : BaseEntity
{
    public required Guid StudioId { get; set; }

    public required string Name { get; set; }

    public string Color { get; set; } = "#5B6CFF";
}
