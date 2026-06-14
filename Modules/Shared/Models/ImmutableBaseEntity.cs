namespace Kuvox.Api.Modules.Shared.Models;

public abstract class ImmutableBaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
