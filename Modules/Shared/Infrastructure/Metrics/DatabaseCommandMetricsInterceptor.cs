using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Prometheus;

namespace Kuvox.Api.Modules.Shared.Infrastructure.Metrics;

public sealed class DatabaseCommandMetricsInterceptor : DbCommandInterceptor
{
    private static readonly Counter CommandCount = Prometheus.Metrics.CreateCounter(
        "kuvox_postgres_commands_total",
        "PostgreSQL commands by module and command type.",
        new CounterConfiguration { LabelNames = ["module", "command_type"] });
    private static readonly Histogram CommandDuration = Prometheus.Metrics.CreateHistogram(
        "kuvox_postgres_command_duration_seconds",
        "PostgreSQL command duration by module and command type.",
        new HistogramConfiguration { LabelNames = ["module", "command_type"] });

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        Record(command, eventData);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        Record(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Record(command, eventData);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        Record(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override object? ScalarExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result)
    {
        Record(command, eventData);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        Record(command, eventData);
        return ValueTask.FromResult(result);
    }

    private static void Record(DbCommand command, CommandExecutedEventData eventData)
    {
        var module = eventData.Context?.GetType().Name.Replace("DbContext", string.Empty) ?? "unknown";
        var commandType = Classify(command.CommandText);
        CommandCount.WithLabels(module.ToLowerInvariant(), commandType).Inc();
        CommandDuration.WithLabels(module.ToLowerInvariant(), commandType)
            .Observe(eventData.Duration.TotalSeconds);
    }

    private static string Classify(string commandText)
    {
        var first = commandText.AsSpan().TrimStart();
        if (first.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)) return "select";
        if (first.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)) return "insert";
        if (first.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)) return "update";
        if (first.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase)) return "delete";
        return "other";
    }
}
