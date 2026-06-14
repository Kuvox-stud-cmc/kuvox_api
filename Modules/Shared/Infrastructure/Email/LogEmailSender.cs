namespace Kuvox.Api.Modules.Shared.Infrastructure.Email;

/// <summary>
/// Dev fallback used when no SendGrid API key is configured. Logs the email (including any
/// verification/reset link in the body) at Information level so the full flow is testable
/// locally without sending real mail. Mirrors the project's <c>StubLLMClient</c> philosophy.
/// </summary>
internal sealed class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    public Task SendAsync(
        string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "[DEV EMAIL] To: {ToEmail} | Subject: {Subject}\n{Body}",
            toEmail, subject, htmlBody);
        return Task.CompletedTask;
    }
}
