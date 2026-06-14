namespace Kuvox.Api.Modules.Shared.Infrastructure.Email;

/// <summary>
/// Transactional email transport. Implemented by <c>SendGridEmailSender</c> (real send) and
/// <c>LogEmailSender</c> (dev fallback that logs the message). Cross-module-safe: any module
/// may depend on this abstraction.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default);
}
