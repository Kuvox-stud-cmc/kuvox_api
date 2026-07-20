using Prometheus;

namespace Kuvox.Api.Modules.Shared.Infrastructure.Caching;

internal static class CacheMetrics
{
    internal static readonly Counter BusinessOperations = Prometheus.Metrics.CreateCounter(
        "kuvox_business_cache_operations_total",
        "Business cache operations by domain and stable outcome.",
        new CounterConfiguration { LabelNames = ["domain", "operation", "outcome"] });
    internal static readonly Histogram BusinessDuration = Prometheus.Metrics.CreateHistogram(
        "kuvox_business_cache_duration_seconds",
        "Business cache operation duration.",
        new HistogramConfiguration { LabelNames = ["domain", "operation"] });
    internal static readonly Histogram BusinessPayloadBytes = Prometheus.Metrics.CreateHistogram(
        "kuvox_business_cache_payload_bytes",
        "Business cache payload sizes.",
        new HistogramConfiguration
        {
            LabelNames = ["domain", "direction"],
            Buckets = [64, 256, 1024, 4096, 16384, 65536, 262144, 1048576]
        });
    internal static readonly Counter BusinessInvalidations = Prometheus.Metrics.CreateCounter(
        "kuvox_business_cache_invalidations_total",
        "Business cache invalidations by domain, kind, and outcome.",
        new CounterConfiguration { LabelNames = ["domain", "kind", "outcome"] });
    internal static readonly Counter BusinessGenerationOperations = Prometheus.Metrics.CreateCounter(
        "kuvox_business_cache_generation_operations_total",
        "Business cache generation operations by domain and stable outcome.",
        new CounterConfiguration { LabelNames = ["domain", "operation", "outcome"] });
    internal static readonly Counter Operations = Prometheus.Metrics.CreateCounter(
        "kuvox_cache_operations_total",
        "Cache operations by stable outcome.",
        new CounterConfiguration { LabelNames = ["service", "operation", "outcome"] });
    internal static readonly Counter RedisCommands = Prometheus.Metrics.CreateCounter(
        "kuvox_redis_commands_total",
        "Redis commands by stable outcome.",
        new CounterConfiguration { LabelNames = ["service", "command", "outcome"] });
    internal static readonly Histogram RedisLatency = Prometheus.Metrics.CreateHistogram(
        "kuvox_redis_command_duration_seconds",
        "Redis command latency.",
        new HistogramConfiguration { LabelNames = ["service", "command"] });
    internal static readonly Histogram PayloadBytes = Prometheus.Metrics.CreateHistogram(
        "kuvox_cache_payload_bytes",
        "Cache payload sizes.",
        new HistogramConfiguration
        {
            LabelNames = ["service", "operation"],
            Buckets = [64, 256, 1024, 4096, 16384, 65536, 262144, 1048576]
        });
    internal static readonly Counter SchemaMisses = Prometheus.Metrics.CreateCounter(
        "kuvox_cache_schema_misses_total",
        "Cache values rejected due to schema incompatibility.",
        new CounterConfiguration { LabelNames = ["service"] });
    internal static readonly Counter OversizedBypasses = Prometheus.Metrics.CreateCounter(
        "kuvox_cache_oversized_bypasses_total",
        "Cache operations bypassed due to oversized payloads.",
        new CounterConfiguration { LabelNames = ["service", "operation"] });
    internal static readonly Gauge CircuitState = Prometheus.Metrics.CreateGauge(
        "kuvox_cache_circuit_state",
        "Cache circuit state: 0 closed, 1 open, 2 half-open.",
        new GaugeConfiguration { LabelNames = ["service"] });
    internal static readonly Counter SingleFlightEvents = Prometheus.Metrics.CreateCounter(
        "kuvox_single_flight_events_total",
        "Single-flight events by service, stable component, and outcome.",
        new CounterConfiguration { LabelNames = ["service", "component", "outcome"] });
    internal static readonly Histogram SingleFlightWait = Prometheus.Metrics.CreateHistogram(
        "kuvox_single_flight_wait_duration_seconds",
        "Time spent waiting to join single-flight work.",
        new HistogramConfiguration { LabelNames = ["service", "component"] });
    internal static readonly Gauge SingleFlightHeldLocks = Prometheus.Metrics.CreateGauge(
        "kuvox_single_flight_held_locks",
        "Locally held distributed single-flight locks.",
        new GaugeConfiguration { LabelNames = ["service", "component"] });
    internal static readonly Counter PrewarmOperations = Prometheus.Metrics.CreateCounter(
        "kuvox_cache_prewarm_operations_total",
        "Cache prewarm operations by stable target and outcome.",
        new CounterConfiguration { LabelNames = ["target", "outcome"] });
    internal static readonly Histogram PrewarmDuration = Prometheus.Metrics.CreateHistogram(
        "kuvox_cache_prewarm_duration_seconds",
        "Cache prewarm duration by stable target.",
        new HistogramConfiguration { LabelNames = ["target"] });

    internal static void RecordOperation(string operation, string outcome) =>
        Operations.WithLabels("api", operation, outcome).Inc();

    internal static void RecordSchemaMiss() => SchemaMisses.WithLabels("api").Inc();

    internal static void SetCircuitState(int state) => CircuitState.WithLabels("api").Set(state);
}
