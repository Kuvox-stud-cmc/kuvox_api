using System.Text;
using Kuvox.Api.Modules.Shared.Infrastructure.RabbitMQ;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Kuvox.Api.Modules.Shared.Infrastructure.Messaging;

internal sealed class RabbitMqRetryHelper(
    IOptions<RabbitMqOptions> rabbitMqOptions,
    IOptions<MessagingOptions> messagingOptions)
{
    private readonly RabbitMqOptions _rabbitMqOptions = rabbitMqOptions.Value;
    private readonly MessagingOptions _messagingOptions = messagingOptions.Value;

    public async Task DeclareRetryTopologyAsync(
        IChannel channel,
        string queueName,
        string routingKey,
        CancellationToken cancellationToken)
    {
        await channel.QueueDeclareAsync(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            queue: queueName,
            exchange: _rabbitMqOptions.ExchangeName,
            routingKey: routingKey,
            arguments: null,
            cancellationToken: cancellationToken);

        for (var index = 0; index < _messagingOptions.RetryDelaysSeconds.Length; index++)
        {
            var attempt = index + 1;
            var retryQueue = RetryQueue(queueName, attempt);
            var args = new Dictionary<string, object?>
            {
                ["x-message-ttl"] = _messagingOptions.RetryDelaysSeconds[index] * 1000,
                ["x-dead-letter-exchange"] = _rabbitMqOptions.ExchangeName,
                ["x-dead-letter-routing-key"] = routingKey
            };

            await channel.QueueDeclareAsync(
                queue: retryQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: args,
                cancellationToken: cancellationToken);

            await channel.QueueBindAsync(
                queue: retryQueue,
                exchange: _rabbitMqOptions.ExchangeName,
                routingKey: retryQueue,
                arguments: null,
                cancellationToken: cancellationToken);
        }

        var dlq = DeadLetterQueue(queueName);
        await channel.QueueDeclareAsync(
            queue: dlq,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            queue: dlq,
            exchange: _rabbitMqOptions.ExchangeName,
            routingKey: dlq,
            arguments: null,
            cancellationToken: cancellationToken);
    }

    public async Task<bool> RetryOrDlqAsync(
        IChannel channel,
        BasicDeliverEventArgs ea,
        string queueName,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var nextAttempt = Attempt(ea) + 1;
        if (nextAttempt > _messagingOptions.MaxAttempts)
        {
            await PublishToDlqAsync(channel, ea, queueName, exception, cancellationToken);
            return false;
        }

        await PublishAsync(
            channel,
            routingKey: RetryQueue(queueName, nextAttempt),
            ea,
            queueName,
            exception,
            attempt: nextAttempt,
            cancellationToken);
        return true;
    }

    public async Task PublishToDlqAsync(
        IChannel channel,
        BasicDeliverEventArgs ea,
        string queueName,
        Exception exception,
        CancellationToken cancellationToken)
    {
        await PublishAsync(
            channel,
            routingKey: DeadLetterQueue(queueName),
            ea,
            queueName,
            exception,
            attempt: Attempt(ea),
            cancellationToken);
    }

    private async Task PublishAsync(
        IChannel channel,
        string routingKey,
        BasicDeliverEventArgs ea,
        string queueName,
        Exception exception,
        int attempt,
        CancellationToken cancellationToken)
    {
        var headers = Headers(ea, queueName, exception, attempt);
        var properties = new BasicProperties
        {
            ContentType = ea.BasicProperties.ContentType ?? "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            Type = ea.BasicProperties.Type,
            Headers = headers,
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        };

        await channel.BasicPublishAsync(
            exchange: _rabbitMqOptions.ExchangeName,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: properties,
            body: ea.Body,
            cancellationToken: cancellationToken);
    }

    private static Dictionary<string, object?> Headers(
        BasicDeliverEventArgs ea,
        string queueName,
        Exception exception,
        int attempt)
    {
        var headers = ea.BasicProperties.Headers?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            ?? new Dictionary<string, object?>();
        headers["x-kuvox-attempt"] = attempt;
        headers["x-kuvox-error-code"] = exception.GetType().Name;
        headers["x-kuvox-error-message"] = exception.Message;
        headers["x-kuvox-failed-at"] = DateTimeOffset.UtcNow.ToString("O");
        headers["x-kuvox-source-queue"] = queueName;
        headers["x-kuvox-event-type"] = ea.BasicProperties.Type ?? string.Empty;
        headers["x-kuvox-event-id"] = HeaderAsString(headers, "x-kuvox-event-id");
        headers["x-kuvox-correlation-id"] = ea.BasicProperties.CorrelationId ?? string.Empty;
        return headers;
    }

    private static int Attempt(BasicDeliverEventArgs ea)
    {
        var headers = ea.BasicProperties.Headers;
        if (headers is null || !headers.TryGetValue("x-kuvox-attempt", out var value))
        {
            return 0;
        }

        return value switch
        {
            int i => i,
            long l => (int)l,
            byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed) => parsed,
            _ => 0
        };
    }

    private static string HeaderAsString(Dictionary<string, object?> headers, string key)
    {
        if (!headers.TryGetValue(key, out var value) || value is null)
        {
            return string.Empty;
        }

        return value switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string RetryQueue(string queueName, int attempt) => $"{queueName}.retry.{attempt}";

    private static string DeadLetterQueue(string queueName) => $"{queueName}.dlq";
}
