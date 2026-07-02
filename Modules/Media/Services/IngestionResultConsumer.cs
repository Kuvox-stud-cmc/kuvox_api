using System.Text.Json;
using Kuvox.Api.Modules.Media.Contracts;
using Kuvox.Api.Modules.Shared.Infrastructure;
using Kuvox.Api.Modules.Shared.Infrastructure.Messaging;
using Kuvox.Api.Modules.Shared.Infrastructure.RabbitMQ;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Kuvox.Api.Modules.Media.Services;

internal sealed class IngestionResultConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    IOptions<MessagingOptions> messagingOptions,
    RabbitMqRetryHelper retryHelper,
    ILogger<IngestionResultConsumer> logger)
    : BackgroundService, IAsyncDisposable
{
    private const string CompletedQueueName = "api.ingestion.completed";
    private const string CompletedRoutingKey = "ingestion.completed";
    private const string FailedQueueName = "api.ingestion.failed";
    private const string FailedRoutingKey = "ingestion.failed";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly RabbitMqOptions _options = options.Value;
    private readonly MessagingOptions _messagingOptions = messagingOptions.Value;
    private IConnection? _connection;
    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndConsumeAsync(stoppingToken);
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Media] Ingestion result consumer stopped unexpectedly.");
                await CloseAsync();
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    private async Task ConnectAndConsumeAsync(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            ClientProvidedName = "kuvox-api-ingestion-consumer",
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
        };

        _connection = await factory.CreateConnectionAsync(cancellationToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await _channel.ExchangeDeclareAsync(
            exchange: _options.ExchangeName,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await DeclareBoundQueueAsync(CompletedQueueName, CompletedRoutingKey, cancellationToken);
        await DeclareBoundQueueAsync(FailedQueueName, FailedRoutingKey, cancellationToken);

        await _channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: (ushort)_messagingOptions.ConsumerPrefetch,
            global: false,
            cancellationToken: cancellationToken);

        await ConsumeCompletedAsync(cancellationToken);
        await ConsumeFailedAsync(cancellationToken);

        logger.LogInformation("[Media] Ingestion result consumer is ready.");
    }

    private async Task DeclareBoundQueueAsync(
        string queueName,
        string routingKey,
        CancellationToken cancellationToken)
    {
        if (_channel is null)
        {
            throw new InvalidOperationException("RabbitMQ channel is not open.");
        }

        await retryHelper.DeclareRetryTopologyAsync(
            _channel,
            queueName,
            routingKey,
            cancellationToken);
    }

    private async Task ConsumeCompletedAsync(CancellationToken cancellationToken)
    {
        var consumer = new AsyncEventingBasicConsumer(_channel!);
        consumer.ReceivedAsync += async (_, ea) =>
            await HandleAsync<IngestionCompletedEvent>(
                ea,
                CompletedQueueName,
                CompletedRoutingKey,
                (media, completed, ct) => media.HandleIngestionCompletedAsync(completed, ct));

        await _channel!.BasicConsumeAsync(
            queue: CompletedQueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);
    }

    private async Task ConsumeFailedAsync(CancellationToken cancellationToken)
    {
        var consumer = new AsyncEventingBasicConsumer(_channel!);
        consumer.ReceivedAsync += async (_, ea) =>
            await HandleAsync<IngestionFailedEvent>(
                ea,
                FailedQueueName,
                FailedRoutingKey,
                (media, failed, ct) => media.HandleIngestionFailedAsync(failed, ct));

        await _channel!.BasicConsumeAsync(
            queue: FailedQueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);
    }

    private async Task HandleAsync<T>(
        BasicDeliverEventArgs ea,
        string queueName,
        string expectedEventType,
        Func<IMediaService, T, CancellationToken, Task> handle)
        where T : class
    {
        if (_channel is null)
        {
            return;
        }

        try
        {
            var message = JsonSerializer.Deserialize<T>(ea.Body.Span, JsonOptions);
            if (message is null || !IsValidMessage(message, expectedEventType))
            {
                await SafeDlqOrRequeueAsync(
                    ea,
                    queueName,
                    new InvalidOperationException("Invalid ingestion result message."));
                return;
            }

            using var scope = scopeFactory.CreateScope();
            var media = scope.ServiceProvider.GetRequiredService<IMediaService>();
            await handle(media, message, CancellationToken.None);
            await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "[Media] Invalid ingestion result message.");
            await SafeDlqOrRequeueAsync(ea, queueName, ex);
        }
        catch (DomainException ex)
        {
            logger.LogWarning(ex, "[Media] Rejected ingestion result message.");
            await SafeDlqOrRequeueAsync(ea, queueName, ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Media] Failed to handle ingestion result message.");
            await SafeRetryOrRequeueAsync(ea, queueName, ex);
        }
    }

    private async Task SafeDlqOrRequeueAsync(
        BasicDeliverEventArgs ea,
        string queueName,
        Exception exception)
    {
        try
        {
            await retryHelper.PublishToDlqAsync(_channel!, ea, queueName, exception, CancellationToken.None);
            await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
        }
        catch (Exception publishEx)
        {
            logger.LogError(publishEx, "[Media] Failed to publish ingestion result to DLQ.");
            await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
        }
    }

    private async Task SafeRetryOrRequeueAsync(
        BasicDeliverEventArgs ea,
        string queueName,
        Exception exception)
    {
        try
        {
            var retried = await retryHelper.RetryOrDlqAsync(
                _channel!,
                ea,
                queueName,
                exception,
                CancellationToken.None);
            await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
            logger.LogWarning(
                "messaging.consumer.{Action} queue={QueueName} error={Error}",
                retried ? "retry" : "dlq",
                queueName,
                exception.Message);
        }
        catch (Exception publishEx)
        {
            logger.LogError(publishEx, "[Media] Failed to publish ingestion result retry/DLQ.");
            await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
        }
    }

    private static bool IsValidMessage<T>(T message, string expectedEventType)
        where T : class =>
        message switch
        {
            IngestionCompletedEvent completed =>
                completed.EventType == expectedEventType
                && completed.EventId != Guid.Empty
                && completed.SourceEventId != Guid.Empty
                && completed.MediaId != Guid.Empty
                && completed.ShotCount >= 0,
            IngestionFailedEvent failed =>
                failed.EventType == expectedEventType
                && failed.EventId != Guid.Empty
                && failed.SourceEventId != Guid.Empty
                && failed.MediaId != Guid.Empty
                && !string.IsNullOrWhiteSpace(failed.ErrorCode)
                && !string.IsNullOrWhiteSpace(failed.ErrorMessage),
            _ => false
        };

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await CloseAsync();
    }

    private async Task CloseAsync()
    {
        if (_channel is not null)
        {
            await _channel.CloseAsync();
            _channel.Dispose();
            _channel = null;
        }

        if (_connection is not null)
        {
            await _connection.CloseAsync();
            _connection.Dispose();
            _connection = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
    }
}
