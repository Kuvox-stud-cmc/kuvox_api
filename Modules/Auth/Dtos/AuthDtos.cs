namespace Kuvox.Api.Modules.Auth.Dtos;

/// <summary>Module-private request/response shapes for the Auth HTTP surface.</summary>
public sealed record RegisterRequest(string Email, string Password, string DisplayName);

public sealed record LoginRequest(string Email, string Password, bool ReplaceExistingSession = false);

public sealed record UserDto(Guid Id, string Email, string DisplayName, string Role, string Plan, bool EmailVerified, DateTimeOffset CreatedAt);

/// <summary>Issued JWT pair returned on login/refresh.</summary>
public sealed record AuthTokenDto(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

public sealed record VerifyEmailRequest(string Token);

/// <summary>Result of consuming an email-verification token: an auto-login token pair plus
/// whether this click is what flipped the account from unverified to verified.</summary>
public sealed record VerifyEmailResult(AuthTokenDto Tokens, bool IsNewlyVerified);

public sealed record ResendVerificationRequest(string Email);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Token, string NewPassword);

public sealed record UserPreferencesDto(
    bool EmailNotificationsEnabled,
    bool ProductUpdatesEnabled,
    bool WeeklyDigestEnabled,
    string DefaultEditorMode
);

public sealed record OnboardingProfileDto(
    string Personality,
    IReadOnlyList<string> CreationGoals,
    DateTimeOffset? OnboardingCompletedAt
);

public sealed record PlanLimitsDto(
    string Plan,
    long StorageBytes,
    int Projects,
    int TeamSeats,
    bool PrioritySupport
);

public sealed record UserSettingsDto(
    UserDto User,
    UserPreferencesDto Preferences,
    OnboardingProfileDto Onboarding,
    PlanLimitsDto PlanLimits
);

public sealed record UpdateProfileRequest(string DisplayName);

public sealed record UpdatePreferencesRequest(
    bool EmailNotificationsEnabled,
    bool ProductUpdatesEnabled,
    bool WeeklyDigestEnabled,
    string DefaultEditorMode
);

public sealed record UpdateOnboardingProfileRequest(
    string Personality,
    IReadOnlyList<string>? CreationGoals
);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
