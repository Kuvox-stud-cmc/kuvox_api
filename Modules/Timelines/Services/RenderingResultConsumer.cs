using System.Text.Json;
using Kuvox.Api.Modules.Shared.Infrastructure;
using Kuvox.Api.Modules.Shared.Infrastructure.Messaging;
using Kuvox.Api.Modules.Shared.Infrastructure.RabbitMQ;
using Kuvox.Api.Modules.Timelines.Contracts;
using Kuvox.Api.Modules.Timelines.Enums;
using Kuvox.Api.Modules.Timelines.Repositories;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Kuvox.Api.Modules.Timelines.Services;

internal sealed class RenderingResultConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    IOptions<MessagingOptions> messagingOptions,
    RabbitMqRetryHelper retryHelper,
    ILogger<RenderingResultConsumer> logger)
    : BackgroundService, IAsyncDisposable
{
    private const string StartedQueueName = "api.rendering.started";
    private const string StartedRoutingKey = "rendering.started";
    private const string CompletedQueueName = "api.rendering.completed";
    private const string CompletedRoutingKey = "rendering.completed";
    private const string FailedQueueName = "api.rendering.failed";
    private const string FailedRoutingKey = "rendering.failed";

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
                logger.LogError(ex, "[Timelines] Rendering result consumer stopped unexpectedly.");
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
            ClientProvidedName = "kuvox-api-rendering-consumer",
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

        await DeclareBoundQueueAsync(StartedQueueName, StartedRoutingKey, cancellationToken);
        await DeclareBoundQueueAsync(CompletedQueueName, CompletedRoutingKey, cancellationToken);
        await DeclareBoundQueueAsync(FailedQueueName, FailedRoutingKey, cancellationToken);

        await _channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: (ushort)_messagingOptions.ConsumerPrefetch,
            global: false,
            cancellationToken: cancellationToken);

        await ConsumeStartedAsync(cancellationToken);
        await ConsumeCompletedAsync(cancellationToken);
        await ConsumeFailedAsync(cancellationToken);

        logger.LogInformation("[Timelines] Rendering result consumer is ready.");
    }

    private async Task DeclareBoundQueueAsync(string queueName, string routingKey, CancellationToken cancellationToken)
    {
        await retryHelper.DeclareRetryTopologyAsync(_channel!, queueName, routingKey, cancellationToken);
    }

    private async Task ConsumeStartedAsync(CancellationToken cancellationToken)
    {
        var consumer = new AsyncEventingBasicConsumer(_channel!);
        consumer.ReceivedAsync += async (_, ea) =>
            await HandleAsync<RenderingStartedEvent>(ea, StartedQueueName, StartedRoutingKey, ApplyStartedAsync);

        await _channel!.BasicConsumeAsync(
            queue: StartedQueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);
    }

    private async Task ConsumeCompletedAsync(CancellationToken cancellationToken)
    {
        var consumer = new AsyncEventingBasicConsumer(_channel!);
        consumer.ReceivedAsync += async (_, ea) =>
            await HandleAsync<RenderingCompletedEvent>(ea, CompletedQueueName, CompletedRoutingKey, ApplyCompletedAsync);

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
            await HandleAsync<RenderingFailedEvent>(ea, FailedQueueName, FailedRoutingKey, ApplyFailedAsync);

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
        Func<ITimelineRepository, IRenderRealtimeNotifier, T, CancellationToken, Task> handle)
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
                await SafeDlqOrRequeueAsync(ea, queueName, new InvalidOperationException("Invalid rendering result message."));
                return;
            }

            using var scope = scopeFactory.CreateScope();
            var timelines = scope.ServiceProvider.GetRequiredService<ITimelineRepository>();
            var realtime = scope.ServiceProvider.GetRequiredService<IRenderRealtimeNotifier>();
            await handle(timelines, realtime, message, CancellationToken.None);
            await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "[Timelines] Invalid rendering result message.");
            await SafeDlqOrRequeueAsync(ea, queueName, ex);
        }
        catch (DomainException ex)
        {
            logger.LogWarning(ex, "[Timelines] Rejected rendering result message.");
            await SafeDlqOrRequeueAsync(ea, queueName, ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Timelines] Failed to handle rendering result message.");
            await SafeRetryOrRequeueAsync(ea, queueName, ex);
        }
    }

    internal static async Task ApplyStartedAsync(
        ITimelineRepository timelines,
        IRenderRealtimeNotifier realtime,
        RenderingStartedEvent started,
        CancellationToken cancellationToken)
    {
        var job = await timelines.GetRenderJobByIdAsync(started.RenderJobId, cancellationToken)
            ?? throw DomainException.NotFound("Render job not found.");
        if (job.Status != RenderStatus.Queued)
        {
            return;
        }

        job.Status = RenderStatus.Rendering;
        job.StartedAt ??= started.StartedAt;
        job.UpdatedAt = started.OccurredAt;
        await timelines.SaveChangesAsync(cancellationToken);
        await realtime.RenderJobUpdatedAsync(job, cancellationToken);
    }

    internal static async Task ApplyCompletedAsync(
        ITimelineRepository timelines,
        IRenderRealtimeNotifier realtime,
        RenderingCompletedEvent completed,
        CancellationToken cancellationToken)
    {
        var job = await timelines.GetRenderJobByIdAsync(completed.RenderJobId, cancellationToken)
            ?? throw DomainException.NotFound("Render job not found.");
        if (job.Status == RenderStatus.Completed)
        {
            return;
        }

        if (job.Status == RenderStatus.Failed)
        {
            return;
        }

        job.Status = RenderStatus.Completed;
        job.OutputBucketName = completed.OutputBucketName;
        job.OutputStorageKey = completed.OutputStorageKey;
        job.OutputContentType = completed.OutputContentType;
        job.OutputSizeBytes = completed.OutputSizeBytes;
        job.FinishedAt = completed.FinishedAt;
        job.ErrorCode = null;
        job.ErrorMessage = null;
        job.UpdatedAt = completed.OccurredAt;
        await timelines.SaveChangesAsync(cancellationToken);
        await realtime.RenderJobUpdatedAsync(job, cancellationToken);
    }

    internal static async Task ApplyFailedAsync(
        ITimelineRepository timelines,
        IRenderRealtimeNotifier realtime,
        RenderingFailedEvent failed,
        CancellationToken cancellationToken)
    {
        var job = await timelines.GetRenderJobByIdAsync(failed.RenderJobId, cancellationToken)
            ?? throw DomainException.NotFound("Render job not found.");
        if (job.Status is RenderStatus.Completed or RenderStatus.Failed)
        {
            return;
        }

        job.Status = RenderStatus.Failed;
        job.ErrorCode = failed.ErrorCode;
        job.ErrorMessage = failed.ErrorMessage;
        job.FinishedAt = failed.FinishedAt;
        job.UpdatedAt = failed.OccurredAt;
        await timelines.SaveChangesAsync(cancellationToken);
        await realtime.RenderJobUpdatedAsync(job, cancellationToken);
    }

    private async Task SafeDlqOrRequeueAsync(BasicDeliverEventArgs ea, string queueName, Exception exception)
    {
        try
        {
            await retryHelper.PublishToDlqAsync(_channel!, ea, queueName, exception, CancellationToken.None);
            await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
        }
        catch (Exception publishEx)
        {
            logger.LogError(publishEx, "[Timelines] Failed to publish rendering result to DLQ.");
            await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
        }
    }

    private async Task SafeRetryOrRequeueAsync(BasicDeliverEventArgs ea, string queueName, Exception exception)
    {
        try
        {
            var retried = await retryHelper.RetryOrDlqAsync(_channel!, ea, queueName, exception, CancellationToken.None);
            await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
            logger.LogWarning(
                "messaging.consumer.{Action} queue={QueueName} error={Error}",
                retried ? "retry" : "dlq",
                queueName,
                exception.Message);
        }
        catch (Exception publishEx)
        {
            logger.LogError(publishEx, "[Timelines] Failed to publish rendering result retry/DLQ.");
            await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
        }
    }

    private static bool IsValidMessage<T>(T message, string expectedEventType)
        where T : class =>
        message switch
        {
            RenderingStartedEvent started =>
                started.EventType == expectedEventType
                && started.EventId != Guid.Empty
                && started.SourceEventId != Guid.Empty
                && started.RenderJobId != Guid.Empty,
            RenderingCompletedEvent completed =>
                completed.EventType == expectedEventType
                && completed.EventId != Guid.Empty
                && completed.SourceEventId != Guid.Empty
                && completed.RenderJobId != Guid.Empty
                && !string.IsNullOrWhiteSpace(completed.OutputBucketName)
                && !string.IsNullOrWhiteSpace(completed.OutputStorageKey)
                && !string.IsNullOrWhiteSpace(completed.OutputContentType)
                && completed.OutputSizeBytes >= 0,
            RenderingFailedEvent failed =>
                failed.EventType == expectedEventType
                && failed.EventId != Guid.Empty
                && failed.SourceEventId != Guid.Empty
                && failed.RenderJobId != Guid.Empty
                && !string.IsNullOrWhiteSpace(failed.ErrorCode),
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
