using Microsoft.Extensions.Options;

namespace Kuvox.Api.Modules.Shared.Infrastructure.Email;

/// <summary>
/// Registers email transport. Binds <see cref="EmailOptions"/> and chooses the sender at
/// startup: <see cref="SendGridEmailSender"/> when an API key is present, otherwise the
/// dev <see cref="LogEmailSender"/>. Cross-cutting infra, so it lives in Shared.
/// </summary>
public static class EmailInfrastructure
{
    public static IServiceCollection AddEmailInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));

        var options = configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>() ?? new EmailOptions();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            services.AddSingleton<IEmailSender, LogEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, SendGridEmailSender>();
        }

        return services;
    }
}
