using System.Text.Json;
using Kuvox.Api.Modules.Auth.Contracts;
using Kuvox.Api.Modules.Auth.Dtos;
using Kuvox.Api.Modules.Auth.Enums;
using Kuvox.Api.Modules.Auth.Models;
using Kuvox.Api.Modules.Auth.Repositories;
using Kuvox.Api.Modules.Shared.Infrastructure;
using Kuvox.Api.Modules.Shared.Infrastructure.Email;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Kuvox.Api.Modules.Auth.Services;

/// <summary>
/// Real Auth business logic: registration, login, refresh-token rotation, logout, the
/// <c>/me</c> projection, email verification, and password reset. Persists via
/// <see cref="IUserRepository"/>, hashes passwords with <see cref="PasswordHasher{TUser}"/>,
/// issues tokens through <see cref="ITokenService"/>, sends transactional email via
/// <see cref="IEmailSender"/>, and publishes <see cref="UserRegisteredEvent"/> via MediatR (Rule 4).
/// </summary>
internal sealed class AuthService(
    IUserRepository users,
    ITokenService tokens,
    IStudioService studioService,
    IPasswordHasher<User> passwordHasher,
    IMediator mediator,
    IEmailSender emailSender,
    IOptions<FrontendOptions> frontendOptions) : IAuthService
{
    private static readonly TimeSpan VerificationTokenLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan ResetTokenLifetime = TimeSpan.FromHours(1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> AllowedCreationGoals = new(StringComparer.Ordinal)
    {
        "youtube",
        "social_clips",
        "highlights",
        "color_grading",
        "podcasts",
        "tutorials",
    };

    private readonly string _frontendBaseUrl = frontendOptions.Value.BaseUrl.TrimEnd('/');

    public async Task<UserDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        if (await users.EmailExistsAsync(email, cancellationToken))
        {
            throw AuthException.Conflict("An account with this email already exists.");
        }

        var user = new User
        {
            Email = email,
            DisplayName = request.DisplayName.Trim(),
            PasswordHash = string.Empty,
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        await users.AddAsync(user, cancellationToken);
        await users.SaveChangesAsync(cancellationToken);

        await mediator.Publish(new UserRegisteredEvent(user.Id, user.Email), cancellationToken);

        // Send verification email (soft gate — signup succeeds regardless).
        await SendVerificationEmailAsync(user, cancellationToken);

        return ToDto(user);
    }

    public async Task<AuthTokenDto> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await users.GetByEmailAsync(email, cancellationToken);

        if (user is null)
        {
            throw AuthException.Unauthorized("Invalid email or password.");
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            throw AuthException.Unauthorized("Invalid email or password.");
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
            await users.SaveChangesAsync(cancellationToken);
        }

        // Hard gate: unverified accounts cannot enter the app.
        if (user.EmailVerifiedAt is null)
        {
            throw AuthException.Forbidden("Please verify your email before signing in.");
        }

        return await IssueNewSessionTokensAsync(user, cancellationToken);
    }

    public async Task<AuthTokenDto> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var hash = tokens.HashToken(refreshToken);
        var stored = await users.GetRefreshTokenByHashAsync(hash, cancellationToken);

        if (stored is null || !stored.IsActive || stored.SessionId is not { } sessionId)
        {
            throw AuthException.Unauthorized("Invalid or expired refresh token.");
        }

        var user = await users.GetByIdAsync(stored.UserId, cancellationToken)
            ?? throw AuthException.Unauthorized("Invalid or expired refresh token.");

        if (user.ActiveSessionId != sessionId)
        {
            throw AuthException.Unauthorized("Invalid or expired refresh token.");
        }

        var memberships = await users.GetStudioMembershipsAsync(user.Id, cancellationToken);
        var (newRefreshToken, refreshHash, refreshExpiresAt) = tokens.CreateRefreshToken();
        var replacement = new RefreshToken
        {
            UserId = user.Id,
            SessionId = sessionId,
            TokenHash = refreshHash,
            ExpiresAt = refreshExpiresAt,
        };

        var rotated = await users.TryRotateRefreshTokenAsync(
            stored.Id,
            sessionId,
            refreshHash,
            replacement,
            cancellationToken);

        if (!rotated)
        {
            throw AuthException.Unauthorized("Invalid or expired refresh token.");
        }

        var (accessToken, expiresAt) = tokens.CreateAccessToken(user, memberships, sessionId);
        return new AuthTokenDto(accessToken, newRefreshToken, expiresAt);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var hash = tokens.HashToken(refreshToken);
        var stored = await users.GetRefreshTokenByHashAsync(hash, cancellationToken);

        if (stored is { SessionId: { } sessionId })
        {
            await users.RevokeSessionAsync(stored.UserId, sessionId, cancellationToken);
        }
        else if (stored is { RevokedAt: null })
        {
            stored.RevokedAt = DateTimeOffset.UtcNow;
            await users.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<UserDto?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        return user is null ? null : ToDto(user);
    }

    public async Task<UserSettingsDto?> GetSettingsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        return user is null ? null : ToSettingsDto(user);
    }

    public async Task<UserDto> UpdateProfileAsync(
        Guid userId,
        UpdateProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken)
            ?? throw AuthException.NotFound("User not found.");

        var displayName = request.DisplayName.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw AuthException.BadRequest("Display name is required.");
        }

        if (displayName.Length > 128)
        {
            throw AuthException.BadRequest("Display name must be 128 characters or fewer.");
        }

        user.DisplayName = displayName;
        await users.SaveChangesAsync(cancellationToken);

        return ToDto(user);
    }

    public async Task<UserPreferencesDto> UpdatePreferencesAsync(
        Guid userId,
        UpdatePreferencesRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken)
            ?? throw AuthException.NotFound("User not found.");

        var defaultEditorMode = NormalizeEditorMode(request.DefaultEditorMode);

        user.EmailNotificationsEnabled = request.EmailNotificationsEnabled;
        user.ProductUpdatesEnabled = request.ProductUpdatesEnabled;
        user.WeeklyDigestEnabled = request.WeeklyDigestEnabled;
        user.DefaultEditorMode = defaultEditorMode;

        await users.SaveChangesAsync(cancellationToken);

        return ToPreferencesDto(user);
    }

    public async Task<OnboardingProfileDto> UpdateOnboardingProfileAsync(
        Guid userId,
        UpdateOnboardingProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken)
            ?? throw AuthException.NotFound("User not found.");

        user.Personality = NormalizePersonality(request.Personality);
        var creationGoals = NormalizeCreationGoals(request.CreationGoals ?? []);
        user.CreationGoalsJson = JsonSerializer.Serialize(creationGoals, JsonOptions);
        user.OnboardingCompletedAt = DateTimeOffset.UtcNow;

        await users.SaveChangesAsync(cancellationToken);

        return ToOnboardingProfileDto(user);
    }

    public async Task ChangePasswordAsync(
        Guid userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken)
            ?? throw AuthException.NotFound("User not found.");

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
        {
            throw AuthException.BadRequest("Password must be at least 8 characters.");
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword);
        if (verification == PasswordVerificationResult.Failed)
        {
            throw AuthException.Unauthorized("Current password is incorrect.");
        }

        user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);
        await users.SaveChangesAsync(cancellationToken);

        await users.RevokeAllRefreshTokensAsync(user.Id, cancellationToken);
    }

    // ── Email verification ──────────────────────────────────────────────────────

    public async Task<VerifyEmailResult> VerifyEmailAsync(string token, CancellationToken cancellationToken = default)
    {
        var hash = tokens.HashToken(token);
        var stored = await users.GetActiveAuthTokenByHashAsync(hash, AuthTokenPurpose.EmailVerification, cancellationToken)
            ?? throw AuthException.BadRequest("Invalid or expired verification token.");

        var user = await users.GetByIdAsync(stored.UserId, cancellationToken)
            ?? throw AuthException.BadRequest("Invalid or expired verification token.");

        // "Brand-new account" proxy: was this click what flipped us from unverified to verified?
        var wasUnverified = user.EmailVerifiedAt is null;

        // Idempotent — if already verified, just consume the token.
        if (wasUnverified)
        {
            user.EmailVerifiedAt = DateTimeOffset.UtcNow;
        }

        stored.UsedAt = DateTimeOffset.UtcNow;
        await users.SaveChangesAsync(cancellationToken);

        if (wasUnverified)
        {
            await studioService.ClaimPendingInvitationsForUserAsync(user.Id, user.Email, cancellationToken);
        }

        // Auto-login: establish a session in the browser that opened the link.
        var issued = await IssueNewSessionTokensAsync(user, cancellationToken);

        return new VerifyEmailResult(issued, wasUnverified);
    }

    public async Task ResendVerificationAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken)
            ?? throw AuthException.NotFound("User not found.");

        // No-op if already verified.
        if (user.EmailVerifiedAt is not null)
        {
            return;
        }

        // Invalidate any prior verification tokens so only the newest is valid.
        await users.InvalidateAuthTokensAsync(userId, AuthTokenPurpose.EmailVerification, cancellationToken);

        await SendVerificationEmailAsync(user, cancellationToken);
    }

    public async Task ResendVerificationByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        // Always succeed silently — no user enumeration. No-op if missing or already verified.
        var user = await users.GetByEmailAsync(email.Trim().ToLowerInvariant(), cancellationToken);
        if (user is null || user.EmailVerifiedAt is not null)
        {
            return;
        }

        // Invalidate any prior verification tokens so only the newest is valid.
        await users.InvalidateAuthTokensAsync(user.Id, AuthTokenPurpose.EmailVerification, cancellationToken);

        await SendVerificationEmailAsync(user, cancellationToken);
    }

    // ── Forgot / reset password ─────────────────────────────────────────────────

    public async Task ForgotPasswordAsync(string email, CancellationToken cancellationToken = default)
    {
        // Always succeed silently — no user enumeration.
        var user = await users.GetByEmailAsync(email.Trim().ToLowerInvariant(), cancellationToken);
        if (user is null)
        {
            return;
        }

        // Invalidate prior reset tokens so only the newest is valid.
        await users.InvalidateAuthTokensAsync(user.Id, AuthTokenPurpose.PasswordReset, cancellationToken);

        var (rawToken, tokenHash, expiresAt) = tokens.CreateSingleUseToken(ResetTokenLifetime);
        await users.AddAuthTokenAsync(new AuthToken
        {
            UserId = user.Id,
            Purpose = AuthTokenPurpose.PasswordReset,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
        }, cancellationToken);
        await users.SaveChangesAsync(cancellationToken);

        var resetLink = $"{_frontendBaseUrl}/reset-password?token={rawToken}";
        await emailSender.SendAsync(
            user.Email,
            "Reset your Kuvox password",
            EmailTemplate.Action(
                "Password reset",
                "Reset your password",
                [
                    $"Hi {user.DisplayName},",
                    "Use this secure link to choose a new Kuvox password and get back to your workspace.",
                ],
                "Reset password",
                resetLink,
                "This link expires in 1 hour. If you did not request this, you can ignore this email."),
            cancellationToken);
    }

    public async Task ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            throw AuthException.BadRequest("Password must be at least 8 characters.");
        }

        var hash = tokens.HashToken(token);
        var stored = await users.GetActiveAuthTokenByHashAsync(hash, AuthTokenPurpose.PasswordReset, cancellationToken)
            ?? throw AuthException.BadRequest("Invalid or expired reset token.");

        var user = await users.GetByIdAsync(stored.UserId, cancellationToken)
            ?? throw AuthException.BadRequest("Invalid or expired reset token.");

        var wasUnverified = user.EmailVerifiedAt is null;
        if (wasUnverified)
        {
            user.EmailVerifiedAt = DateTimeOffset.UtcNow;
        }

        user.PasswordHash = passwordHasher.HashPassword(user, newPassword);
        stored.UsedAt = DateTimeOffset.UtcNow;

        await users.SaveChangesAsync(cancellationToken);

        if (wasUnverified)
        {
            await studioService.ClaimPendingInvitationsForUserAsync(user.Id, user.Email, cancellationToken);
        }

        // Force re-login everywhere by revoking all existing refresh tokens.
        await users.RevokeAllRefreshTokensAsync(user.Id, cancellationToken);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private async Task SendVerificationEmailAsync(User user, CancellationToken cancellationToken)
    {
        var (rawToken, tokenHash, expiresAt) = tokens.CreateSingleUseToken(VerificationTokenLifetime);
        await users.AddAuthTokenAsync(new AuthToken
        {
            UserId = user.Id,
            Purpose = AuthTokenPurpose.EmailVerification,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
        }, cancellationToken);
        await users.SaveChangesAsync(cancellationToken);

        var verifyLink = $"{_frontendBaseUrl}/verify-email?token={rawToken}";
        await emailSender.SendAsync(
            user.Email,
            "Verify your Kuvox email",
            EmailTemplate.Action(
                "Account verification",
                "Verify your email",
                [
                    $"Hi {user.DisplayName},",
                    "Welcome to Kuvox. Confirm this address to activate your account and start editing.",
                ],
                "Verify email",
                verifyLink,
                "This link expires in 24 hours. If you did not create a Kuvox account, you can ignore this email."),
            cancellationToken);
    }

    private async Task<AuthTokenDto> IssueNewSessionTokensAsync(User user, CancellationToken cancellationToken)
    {
        var memberships = await users.GetStudioMembershipsAsync(user.Id, cancellationToken);
        var sessionId = Guid.CreateVersion7();
        var (accessToken, expiresAt) = tokens.CreateAccessToken(user, memberships, sessionId);
        var (refreshToken, refreshHash, refreshExpiresAt) = tokens.CreateRefreshToken();
        var created = await users.TryCreateSessionAsync(
            user.Id,
            sessionId,
            new RefreshToken
            {
                UserId = user.Id,
                SessionId = sessionId,
                TokenHash = refreshHash,
                ExpiresAt = refreshExpiresAt,
            },
            cancellationToken);

        if (!created)
        {
            throw AuthException.Conflict("This account is already signed in on another device.");
        }

        return new AuthTokenDto(accessToken, refreshToken, expiresAt);
    }

    private static UserDto ToDto(User user) =>
        new(user.Id, user.Email, user.DisplayName, "user", user.Plan.ToString(), user.EmailVerifiedAt is not null, user.CreatedAt);

    private static UserSettingsDto ToSettingsDto(User user) =>
        new(ToDto(user), ToPreferencesDto(user), ToOnboardingProfileDto(user), ToPlanLimitsDto(user.Plan));

    private static UserPreferencesDto ToPreferencesDto(User user) =>
        new(
            user.EmailNotificationsEnabled,
            user.ProductUpdatesEnabled,
            user.WeeklyDigestEnabled,
            user.DefaultEditorMode);

    private static OnboardingProfileDto ToOnboardingProfileDto(User user) =>
        new(
            PersonalityKey(user.Personality),
            DeserializeCreationGoals(user.CreationGoalsJson),
            user.OnboardingCompletedAt);

    private static PlanLimitsDto ToPlanLimitsDto(UserPlan plan) =>
        plan switch
        {
            UserPlan.Creator => new(plan.ToString(), 100L * 1024L * 1024L * 1024L, 500, 5, true),
            _ => new(plan.ToString(), 5L * 1024L * 1024L * 1024L, 10, 1, false),
        };

    private static UserPersonality NormalizePersonality(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw AuthException.BadRequest("Onboarding personality is required.");
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "creator" or "editor" => UserPersonality.Creator,
            "casual" => UserPersonality.Casual,
            "professional" => UserPersonality.Professional,
            _ => throw AuthException.BadRequest("Invalid onboarding personality."),
        };
    }

    private static IReadOnlyList<string> NormalizeCreationGoals(IReadOnlyList<string> values)
    {
        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var goal = value.Trim().ToLowerInvariant();
            if (!AllowedCreationGoals.Contains(goal))
            {
                throw AuthException.BadRequest("Invalid onboarding creation goal.");
            }

            if (seen.Add(goal))
            {
                normalized.Add(goal);
            }
        }

        return normalized;
    }

    private static string PersonalityKey(UserPersonality personality) =>
        personality switch
        {
            UserPersonality.Creator => "creator",
            UserPersonality.Professional => "professional",
            _ => "casual",
        };

    private static IReadOnlyList<string> DeserializeCreationGoals(string creationGoalsJson)
    {
        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<string>>(creationGoalsJson, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string NormalizeEditorMode(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "manual" or "ai" ? normalized : "manual";
    }
}
