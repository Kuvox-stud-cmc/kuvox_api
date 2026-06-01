namespace Kuvox.Api.Modules.Auth.Contracts;

/// <summary>Minimal user projection safe to share with other modules (Rule 2).</summary>
public sealed record UserSummary(Guid Id, string Email, string DisplayName);
