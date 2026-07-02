using System.Text.Json;
using Kuvox.Api.Modules.Media.Repositories;
using Kuvox.Api.Modules.Shared.Infrastructure.RabbitMQ;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kuvox.Api.Modules.Shared.Infrastructure.Messaging;

internal sealed class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IRabbitMqPublisher publisher,
    IOptions<MessagingOptions> options,
    ILogger<OutboxDispatcher> logger)
    : BackgroundService
{
    private readonly MessagingOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchDueMessagesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "messaging.outbox.dispatch_loop_failed");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_options.OutboxPollIntervalSeconds),
                stoppingToken);
        }
    }

    private async Task DispatchDueMessagesAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
        var now = DateTimeOffset.UtcNow;
        var lockUntil = now.AddMinutes(2);

        var messages = await db.OutboxMessages
            .Where(message =>
                message.Status == OutboxMessageStatus.Pending
                && message.NextAttemptAt <= now
                && (message.LockedUntil == null || message.LockedUntil < now))
            .OrderBy(message => message.CreatedAt)
            .Take(_options.OutboxBatchSize)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
        {
            return;
        }

        foreach (var message in messages)
        {
            message.LockedUntil = lockUntil;
        }
        await db.SaveChangesAsync(cancellationToken);

        foreach (var message in messages)
        {
            await DispatchOneAsync(db, message, cancellationToken);
        }
    }

    private async Task DispatchOneAsync(
        MediaDbContext db,
        OutboxMessage message,
        CancellationToken cancellationToken)
    {
        try
        {
            await publisher.PublishJsonAsync(
                message.RoutingKey,
                message.PayloadJson,
                HeadersFromJson(message.HeadersJson),
                cancellationToken);

            message.Status = OutboxMessageStatus.Published;
            message.PublishedAt = DateTimeOffset.UtcNow;
            message.LockedUntil = null;
            message.LastError = null;
            logger.LogInformation(
                "messaging.outbox.published routing_key={RoutingKey} event_type={EventType} outbox_id={OutboxId}",
                message.RoutingKey,
                message.EventType,
                message.Id);
        }
        catch (Exception ex)
        {
            message.AttemptCount++;
            message.LockedUntil = null;
            message.LastError = ex.Message;

            if (message.AttemptCount >= _options.MaxAttempts)
            {
                message.Status = OutboxMessageStatus.Dead;
                logger.LogError(
                    ex,
                    "messaging.outbox.dead routing_key={RoutingKey} event_type={EventType} outbox_id={OutboxId}",
                    message.RoutingKey,
                    message.EventType,
                    message.Id);
            }
            else
            {
                message.NextAttemptAt = DateTimeOffset.UtcNow
                    + _options.RetryDelayForAttempt(message.AttemptCount);
                logger.LogWarning(
                    ex,
                    "messaging.outbox.retry routing_key={RoutingKey} event_type={EventType} outbox_id={OutboxId} attempt={Attempt}",
                    message.RoutingKey,
                    message.EventType,
                    message.Id,
                    message.AttemptCount);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static IReadOnlyDictionary<string, object?> HeadersFromJson(string headersJson)
    {
        if (string.IsNullOrWhiteSpace(headersJson) || headersJson == "{}")
        {
            return new Dictionary<string, object?>();
        }

        return JsonSerializer.Deserialize<Dictionary<string, object?>>(headersJson)
            ?? new Dictionary<string, object?>();
    }
}
