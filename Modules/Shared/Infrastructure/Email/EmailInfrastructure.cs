namespace Kuvox.Api.Modules.Shared.Infrastructure.Email;

/// <summary>
/// Registers email transport. SendGrid is attempted first; Brevo is attempted as fallback.
/// If no provider is configured, the dev log sender is used.
/// </summary>
public static class EmailInfrastructure
{
    public static IServiceCollection AddEmailInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.Configure<SendGridEmailOptions>(configuration.GetSection(SendGridEmailOptions.SectionName));
        services.Configure<BrevoEmailOptions>(configuration.GetSection(BrevoEmailOptions.SectionName));
        services.AddSingleton<LogEmailSender>();
        services.AddSingleton<IEmailProviderStrategy, SendGridEmailProviderStrategy>();
        services.AddSingleton<IEmailProviderStrategy, BrevoEmailProviderStrategy>();
        services.AddSingleton<IEmailSender, FallbackEmailSender>();

        return services;
    }
}
