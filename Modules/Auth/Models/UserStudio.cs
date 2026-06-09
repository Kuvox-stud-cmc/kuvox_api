using Kuvox.Api.Modules.Shared.Models;
using Kuvox.Api.Modules.Auth.Enums;

namespace Kuvox.Api.Modules.Auth.Models;

public sealed class UserStudio : JunctionBaseEntity
{
    public required Guid UserId { get; set; }

    public required Guid StudioId { get; set; }

    public required UserStudioRole Role { get; set; }
}