using Kuvox.Api.Modules.Auth.Enums;

namespace Kuvox.Api.Modules.Auth.Dtos;

/// <summary>A studio (team) the caller belongs to, plus the caller's role in it.</summary>
public sealed record StudioDto(Guid Id, string Name, UserStudioRole Role);

public sealed record CreateStudioRequest(string Name);

/// <summary>A studio member (joined with the user record for display).</summary>
public sealed record StudioMemberDto(Guid UserId, string Email, string DisplayName, UserStudioRole Role);

/// <summary>Adds an existing user (looked up by email) to a studio.</summary>
public sealed record AddStudioMemberRequest(string Email, UserStudioRole Role);

public sealed record UpdateStudioMemberRequest(UserStudioRole Role);
