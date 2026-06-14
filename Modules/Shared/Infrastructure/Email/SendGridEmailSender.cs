using Microsoft.Extensions.Options;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace Kuvox.Api.Modules.Shared.Infrastructure.Email;

/// <summary>
/// Sends transactional email via SendGrid. Throws if SendGrid returns a non-2xx status so the
/// caller (Auth flows) can surface a failure. Selected only when an API key is configured —
/// otherwise <see cref="LogEmailSender"/> is registered instead.
/// </summary>
internal sealed class SendGridEmailSender(
    IOptions<EmailOptions> options,
    ILogger<SendGridEmailSender> logger) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public async Task SendAsync(
        string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        var client = new SendGridClient(_options.ApiKey);
        var message = MailHelper.CreateSingleEmail(
            new EmailAddress(_options.FromEmail, _options.FromName),
            new EmailAddress(toEmail),
            subject,
            plainTextContent: null,
            htmlContent: htmlBody);

        var response = await client.SendEmailAsync(message, cancellationToken);

        if ((int)response.StatusCode >= 300)
        {
            var body = await response.Body.ReadAsStringAsync(cancellationToken);
            logger.LogError(
                "SendGrid send to {ToEmail} failed with {StatusCode}: {Body}",
                toEmail, response.StatusCode, body);
            throw new InvalidOperationException($"SendGrid send failed with status {response.StatusCode}.");
        }

        logger.LogInformation("Sent '{Subject}' email to {ToEmail} via SendGrid.", subject, toEmail);
    }
}
