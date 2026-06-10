namespace Kuvox.Api.Modules.Shared.Models;

/// <summary>
/// Base type for all module entities. Keeps the identity + audit columns identical
/// across modules without coupling the modules to one another.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
