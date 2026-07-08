using brevo_csharp.Api;
using brevo_csharp.Client;
using BrevoSendSmtpEmail = brevo_csharp.Model.SendSmtpEmail;
using BrevoSendSmtpEmailSender = brevo_csharp.Model.SendSmtpEmailSender;
using BrevoSendSmtpEmailTo = brevo_csharp.Model.SendSmtpEmailTo;
using Microsoft.Extensions.Options;

namespace Kuvox.Api.Modules.Shared.Infrastructure.Email;

/// <summary>
/// Fallback transactional email provider backed by Brevo's official C# SDK.
/// </summary>
internal sealed class BrevoEmailProviderStrategy(
    IOptions<EmailOptions> options,
    IOptions<BrevoEmailOptions> brevoOptions,
    ILogger<BrevoEmailProviderStrategy> logger) : IEmailProviderStrategy
{
    private readonly EmailOptions _options = options.Value;
    private readonly BrevoEmailOptions _brevoOptions = brevoOptions.Value;

    public string Name => "Brevo";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_brevoOptions.ApiKey);

    public async Task SendAsync(
        string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Sending email from {FromEmail} named {FromName} via Brevo to {ToEmail} with subject '{Subject}'.", _options.FromEmail, _options.FromName, toEmail, subject);

        cancellationToken.ThrowIfCancellationRequested();

        var configuration = new Configuration();
        configuration.ApiKey.Add("api-key", _brevoOptions.ApiKey);

        var api = new TransactionalEmailsApi(configuration);
        var message = new BrevoSendSmtpEmail
        {
            Sender = new BrevoSendSmtpEmailSender(
                email: _options.FromEmail,
                name: _options.FromName),
            To =
            [
                new BrevoSendSmtpEmailTo(email: toEmail),
            ],
            Subject = subject,
            HtmlContent = htmlBody,
        };

        try
        {
            await api.SendTransacEmailAsync(message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ApiException ex)
        {
            object? statusCodeValue = ex.ErrorCode;
            object? bodyValue = ex.ErrorContent;
            var statusCode = statusCodeValue?.ToString();
            var body = bodyValue?.ToString();
            logger.LogError(
                ex,
                "Brevo send to {ToEmail} failed with {StatusCode}: {Body}",
                toEmail,
                ex.ErrorCode,
                body);
            throw;
        }
    }
}
