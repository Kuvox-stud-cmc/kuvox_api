using Kuvox.Api.Modules.Auth.Enums;
using Kuvox.Api.Modules.Shared.Models;

namespace Kuvox.Api.Modules.Auth.Models;

public sealed class AuditLogEntry : ImmutableBaseEntity
{
    public Guid? ActorUserId { get; set; }

    public required string WorkspaceKind { get; set; }

    public required Guid WorkspaceId { get; set; }

    public required StudioAuditCategory Category { get; set; }

    public required string Action { get; set; }

    public required string TargetKind { get; set; }

    public Guid? TargetId { get; set; }

    public required string Summary { get; set; }

    public string? MetadataJson { get; set; }
}
