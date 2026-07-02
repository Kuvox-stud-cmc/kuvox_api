namespace Kuvox.Api.Modules.Shared.Infrastructure.RabbitMQ;

public interface IRabbitMqPublisher
{
  Task PublishAsync<T>(
    string routingKey,
    T message,
    CancellationToken cancellationToken = default
  );
}
