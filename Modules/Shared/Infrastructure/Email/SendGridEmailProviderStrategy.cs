using Microsoft.Extensions.Options;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace Kuvox.Api.Modules.Shared.Infrastructure.Email;

/// <summary>
/// Primary transactional email provider. Throws provider-level failures so
/// <see cref="FallbackEmailSender"/> can try the next configured provider.
/// </summary>
internal sealed class SendGridEmailProviderStrategy(
    IOptions<EmailOptions> options,
    IOptions<SendGridEmailOptions> sendGridOptions,
    ILogger<SendGridEmailProviderStrategy> logger) : IEmailProviderStrategy
{
    private readonly EmailOptions _options = options.Value;
    private readonly SendGridEmailOptions _sendGridOptions = sendGridOptions.Value;

    public string Name => "SendGrid";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_sendGridOptions.ApiKey);

    public async Task SendAsync(
        string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Sending email from {FromEmail} named {FromName} via SendGrid to {ToEmail} with subject '{Subject}'.", _options.FromEmail, _options.FromName, toEmail, subject);

        var client = new SendGridClient(_sendGridOptions.ApiKey);
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

        logger.LogDebug("SendGrid accepted '{Subject}' email to {ToEmail}.", subject, toEmail);
    }
}
