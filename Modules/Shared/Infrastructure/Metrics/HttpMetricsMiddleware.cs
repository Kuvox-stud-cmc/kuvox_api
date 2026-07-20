using System.Diagnostics;
using Prometheus;

namespace Kuvox.Api.Modules.Shared.Infrastructure.Metrics;

public sealed class HttpMetricsMiddleware(RequestDelegate next)
{
    private static readonly Counter Requests = Prometheus.Metrics.CreateCounter(
        "kuvox_http_requests_total",
        "HTTP requests by normalized route template.",
        new CounterConfiguration { LabelNames = ["service", "method", "route", "status"] });
    private static readonly Histogram Duration = Prometheus.Metrics.CreateHistogram(
        "kuvox_http_request_duration_seconds",
        "HTTP request duration by normalized route template.",
        new HistogramConfiguration { LabelNames = ["service", "method", "route"] });

    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        finally
        {
            var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
            var method = context.Request.Method;
            Requests.WithLabels("api", method, route, context.Response.StatusCode.ToString()).Inc();
            Duration.WithLabels("api", method, route)
                .Observe(Stopwatch.GetElapsedTime(started).TotalSeconds);
        }
    }
}
