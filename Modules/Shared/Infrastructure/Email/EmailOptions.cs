namespace Kuvox.Api.Modules.Shared.Infrastructure.Email;

/// <summary>
/// SendGrid + link-building settings, bound from the <c>Email</c> configuration section.
/// When <see cref="ApiKey"/> is empty the app uses a dev/log sender instead of calling
/// SendGrid, so the verification/reset flows are testable without a key or external send.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>SendGrid API key. Empty in dev → log-only sender. Inject via env in real envs.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public string FromEmail { get; set; } = "no-reply@kuvox.app";

    public string FromName { get; set; } = "Kuvox";
}
