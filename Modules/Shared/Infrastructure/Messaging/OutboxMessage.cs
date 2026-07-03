using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kuvox.Api.Modules.Shared.Infrastructure.Messaging;

public enum OutboxMessageStatus
{
    Pending,
    Published,
    Dead
}

public sealed class OutboxMessage
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public Guid Id { get; init; } = Guid.NewGuid();
    public string DedupeKey { get; init; } = string.Empty;
    public string Transport { get; set; } = "rabbitmq";
    public string Exchange { get; set; } = string.Empty;
    public string RoutingKey { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string HeadersJson { get; set; } = "{}";
    public OutboxMessageStatus Status { get; set; } = OutboxMessageStatus.Pending;
    public int AttemptCount { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LockedUntil { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PublishedAt { get; set; }

    public static OutboxMessage Create<T>(
        string dedupeKey,
        string exchange,
        string routingKey,
        string eventType,
        T payload,
        IReadOnlyDictionary<string, object?>? headers = null)
    {
        return new OutboxMessage
        {
            DedupeKey = dedupeKey,
            Exchange = exchange,
            RoutingKey = routingKey,
            EventType = eventType,
            PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
            HeadersJson = headers is null
                ? "{}"
                : JsonSerializer.Serialize(headers, JsonOptions)
        };
    }
}
