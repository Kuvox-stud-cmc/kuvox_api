using Kuvox.Api.Modules.Shared.Infrastructure.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Tests;

public sealed class EmailFallbackSenderTests
{
    [Fact]
    public async Task SendAsync_uses_sendgrid_first_and_skips_brevo_when_sendgrid_succeeds()
    {
        var sendGrid = new FakeEmailProviderStrategy("SendGrid", isConfigured: true);
        var brevo = new FakeEmailProviderStrategy("Brevo", isConfigured: true);
        var sender = CreateSender(sendGrid, brevo);

        await sender.SendAsync("user@example.com", "Subject", "<p>Hello</p>");

        Assert.Equal(1, sendGrid.Calls);
        Assert.Equal(0, brevo.Calls);
    }

    [Fact]
    public async Task SendAsync_falls_back_to_brevo_when_sendgrid_fails()
    {
        var sendGrid = new FakeEmailProviderStrategy(
            "SendGrid",
            isConfigured: true,
            failure: new InvalidOperationException("sendgrid failed"));
        var brevo = new FakeEmailProviderStrategy("Brevo", isConfigured: true);
        var sender = CreateSender(sendGrid, brevo);

        await sender.SendAsync("user@example.com", "Subject", "<p>Hello</p>");

        Assert.Equal(1, sendGrid.Calls);
        Assert.Equal(1, brevo.Calls);
    }

    [Fact]
    public async Task SendAsync_throws_when_all_configured_providers_fail()
    {
        var sendGrid = new FakeEmailProviderStrategy(
            "SendGrid",
            isConfigured: true,
            failure: new InvalidOperationException("sendgrid failed"));
        var brevo = new FakeEmailProviderStrategy(
            "Brevo",
            isConfigured: true,
            failure: new InvalidOperationException("brevo failed"));
        var sender = CreateSender(sendGrid, brevo);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sender.SendAsync("user@example.com", "Subject", "<p>Hello</p>"));

        Assert.Contains("All configured email providers failed", ex.Message, StringComparison.Ordinal);
        Assert.Equal(1, sendGrid.Calls);
        Assert.Equal(1, brevo.Calls);
    }

    [Fact]
    public async Task SendAsync_uses_brevo_when_sendgrid_is_not_configured()
    {
        var sendGrid = new FakeEmailProviderStrategy("SendGrid", isConfigured: false);
        var brevo = new FakeEmailProviderStrategy("Brevo", isConfigured: true);
        var sender = CreateSender(sendGrid, brevo);

        await sender.SendAsync("user@example.com", "Subject", "<p>Hello</p>");

        Assert.Equal(0, sendGrid.Calls);
        Assert.Equal(1, brevo.Calls);
    }

    [Fact]
    public async Task SendAsync_logs_email_when_no_provider_is_configured()
    {
        var logEmailLogger = new ListLogger<LogEmailSender>();
        var sendGrid = new FakeEmailProviderStrategy("SendGrid", isConfigured: false);
        var brevo = new FakeEmailProviderStrategy("Brevo", isConfigured: false);
        var sender = CreateSender(logEmailLogger, sendGrid, brevo);

        await sender.SendAsync("user@example.com", "Subject", "<p>Hello</p>");

        Assert.Equal(0, sendGrid.Calls);
        Assert.Equal(0, brevo.Calls);
        Assert.Contains(logEmailLogger.Messages, message => message.Contains("[DEV EMAIL]", StringComparison.Ordinal));
    }

    private static FallbackEmailSender CreateSender(params IEmailProviderStrategy[] providers) =>
        CreateSender(new ListLogger<LogEmailSender>(), providers);

    private static FallbackEmailSender CreateSender(
        ListLogger<LogEmailSender> logEmailLogger,
        params IEmailProviderStrategy[] providers) =>
        new(
            providers,
            new LogEmailSender(logEmailLogger),
            NullLogger<FallbackEmailSender>.Instance);

    private sealed class FakeEmailProviderStrategy(
        string name,
        bool isConfigured,
        Exception? failure = null) : IEmailProviderStrategy
    {
        public string Name { get; } = name;

        public bool IsConfigured { get; } = isConfigured;

        public int Calls { get; private set; }

        public Task SendAsync(
            string toEmail,
            string subject,
            string htmlBody,
            CancellationToken cancellationToken = default)
        {
            Calls++;

            return failure is null ? Task.CompletedTask : Task.FromException(failure);
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            NoopDisposable.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
