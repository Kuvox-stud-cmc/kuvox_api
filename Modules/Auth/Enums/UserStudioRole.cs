namespace Kuvox.Api.Modules.Auth.Enums;

public enum UserStudioRole
{
    Owner = 0,
    Admin = 1,
    Member = 2,
    Viewer = 3,

    // Backward-compatible parse alias for older persisted string enum values.
    User = Member
}
