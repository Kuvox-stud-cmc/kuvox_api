namespace Kuvox.Api.Modules.Shared.Infrastructure.Email;

internal interface IEmailProviderStrategy
{
    string Name { get; }

    bool IsConfigured { get; }

    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default);
}
