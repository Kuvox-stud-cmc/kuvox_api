namespace Kuvox.Api.Modules.Shared.Infrastructure.Email;

internal sealed class FallbackEmailSender(
    IEnumerable<IEmailProviderStrategy> providers,
    LogEmailSender logEmailSender,
    ILogger<FallbackEmailSender> logger) : IEmailSender
{
    private readonly IReadOnlyList<IEmailProviderStrategy> _providers = providers.ToList();

    public async Task SendAsync(
        string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        var configuredProviders = _providers.Where(provider => provider.IsConfigured).ToList();

        if (configuredProviders.Count == 0)
        {
            logger.LogInformation(
                "No real email provider is configured; logging '{Subject}' email to {ToEmail}.",
                subject,
                toEmail);
            await logEmailSender.SendAsync(toEmail, subject, htmlBody, cancellationToken);
            return;
        }

        Exception? lastFailure = null;

        foreach (var provider in configuredProviders)
        {
            try
            {
                await provider.SendAsync(toEmail, subject, htmlBody, cancellationToken);
                logger.LogInformation(
                    "Sent '{Subject}' email to {ToEmail} via {Provider}.",
                    subject,
                    toEmail,
                    provider.Name);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastFailure = ex;
                logger.LogError(
                    ex,
                    "{Provider} failed to send '{Subject}' email to {ToEmail}.",
                    provider.Name,
                    subject,
                    toEmail);
            }
        }

        throw new InvalidOperationException(
            $"All configured email providers failed to send '{subject}' email to {toEmail}.",
            lastFailure);
    }
}
