namespace Kuvox.Api.Modules.Shared.Infrastructure.Email;

/// <summary>
/// Shared sender identity for transactional email, bound from the <c>Email</c>
/// configuration section. Provider credentials live in provider-specific sections.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string FromEmail { get; set; } = "no-reply@kuvox.app";

    public string FromName { get; set; } = "Kuvox";
}

public sealed class SendGridEmailOptions
{
    public const string SectionName = "SendGrid:Email";

    public string ApiKey { get; set; } = string.Empty;
}

public sealed class BrevoEmailOptions
{
    public const string SectionName = "Brevo:Email";

    public string ApiKey { get; set; } = string.Empty;
}
