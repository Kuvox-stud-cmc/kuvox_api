using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Kuvox.Api.Modules.Shared.Infrastructure.RabbitMQ;

internal sealed class RabbitMqPublisher : IRabbitMqPublisher, IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ConnectionFactory _factory;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly SemaphoreSlim _publishLock = new(1, 1);

    private IConnection? _connection;
    private IChannel? _channel;
    private bool _exchangeDeclared;
    private readonly HashSet<string> _declaredQueues = [];

    public RabbitMqPublisher(IOptions<RabbitMqOptions> options)
    {
        _options = options.Value;

        _factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            ClientProvidedName = "kuvox-api-publisher",
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
        };
    }

    public async Task PublishAsync<T>(
        string routingKey,
        T message,
        CancellationToken cancellationToken = default)
    {
        var body = Serialize(message);

        await _publishLock.WaitAsync(cancellationToken);

        try
        {
            var channel = await GetOpenChannelAsync(cancellationToken);

            await EnsureTopologyAsync(channel, routingKey, cancellationToken);

            var properties = new BasicProperties
            {
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                Type = routingKey,
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            };

            await channel.BasicPublishAsync(
                exchange: _options.ExchangeName,
                routingKey: routingKey,
                mandatory: true,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);
        }
        finally
        {
            _publishLock.Release();
        }
    }

    private async Task<IChannel> GetOpenChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        await _lifecycleLock.WaitAsync(cancellationToken);

        try
        {
            if (_channel is { IsOpen: true })
            {
                return _channel;
            }

            if (_connection is not { IsOpen: true })
            {
                _connection?.Dispose();

                _connection = await _factory.CreateConnectionAsync(cancellationToken);
                _exchangeDeclared = false;
                _declaredQueues.Clear();
            }

            _channel?.Dispose();

            _channel = await _connection.CreateChannelAsync(
                new CreateChannelOptions(
                  publisherConfirmationsEnabled: true,
                  publisherConfirmationTrackingEnabled: true
                ),
                cancellationToken: cancellationToken);

            _exchangeDeclared = false;
            _declaredQueues.Clear();

            return _channel;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private async Task EnsureTopologyAsync(
        IChannel channel,
        string routingKey,
        CancellationToken cancellationToken)
    {
        if (!_exchangeDeclared)
        {
            await channel.ExchangeDeclareAsync(
                exchange: _options.ExchangeName,
                type: ExchangeType.Direct,
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken);

            _exchangeDeclared = true;
        }

        if (_declaredQueues.Contains(routingKey))
        {
            return;
        }

        await channel.QueueDeclareAsync(
            queue: routingKey,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            queue: routingKey,
            exchange: _options.ExchangeName,
            routingKey: routingKey,
            arguments: null,
            cancellationToken: cancellationToken);

        _declaredQueues.Add(routingKey);
    }

    private static ReadOnlyMemory<byte> Serialize<T>(T message)
    {
        var json = JsonSerializer.Serialize(message, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        });

        return Encoding.UTF8.GetBytes(json);
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycleLock.WaitAsync();

        try
        {
            if (_channel is not null)
            {
                await _channel.CloseAsync();
                _channel.Dispose();
            }

            if (_connection is not null)
            {
                await _connection.CloseAsync();
                _connection.Dispose();
            }
        }
        finally
        {
            _lifecycleLock.Release();
            _publishLock.Dispose();
            _lifecycleLock.Dispose();
        }
    }
}
