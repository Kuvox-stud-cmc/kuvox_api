using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace Kuvox.Api.Modules.Shared.Infrastructure.Caching;

public enum CachePrewarmKind
{
    StudioSettings,
    TaskReferences
}

public sealed record CachePrewarmRequest(CachePrewarmKind Kind, Guid StudioId, string Generation);

public interface ICachePrewarmTarget
{
    CachePrewarmKind Kind { get; }
    Task<bool> PrewarmAsync(CachePrewarmRequest request, CancellationToken cancellationToken);
}

public sealed class CachePrewarmQueue
{
    private readonly Channel<CachePrewarmRequest> _channel;
    private readonly CachingOptions _options;

    public CachePrewarmQueue(IOptions<CachingOptions> options)
    {
        _options = options.Value;
        _channel = Channel.CreateBounded<CachePrewarmRequest>(new BoundedChannelOptions(
            _options.PrewarmQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    public bool TryQueue(CachePrewarmRequest request)
    {
        if (!IsEnabled(request.Kind))
        {
            CacheMetrics.PrewarmOperations.WithLabels(Name(request.Kind), "disabled").Inc();
            return false;
        }
        if (_channel.Writer.TryWrite(request))
        {
            CacheMetrics.PrewarmOperations.WithLabels(Name(request.Kind), "queued").Inc();
            return true;
        }
        CacheMetrics.PrewarmOperations.WithLabels(Name(request.Kind), "dropped").Inc();
        return false;
    }

    public IAsyncEnumerable<CachePrewarmRequest> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    private bool IsEnabled(CachePrewarmKind kind) =>
        _options.Enabled && _options.BusinessReads.Enabled && kind switch
        {
            CachePrewarmKind.StudioSettings => _options.Studio.Enabled
                && _options.StudioSettingsPrewarmEnabled,
            CachePrewarmKind.TaskReferences => _options.Tasks.Enabled
                && _options.TaskReferencePrewarmEnabled,
            _ => false,
        };

    internal static string Name(CachePrewarmKind kind) => kind switch
    {
        CachePrewarmKind.StudioSettings => "studio-settings",
        CachePrewarmKind.TaskReferences => "task-references",
        _ => "unknown",
    };
}

public sealed class CachePrewarmWorker(
    CachePrewarmQueue queue,
    IServiceScopeFactory scopes,
    CacheGenerationManager generations,
    IOptions<CachingOptions> options,
    ILogger<CachePrewarmWorker> logger) : BackgroundService
{
    private readonly CachingOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await QueueStartupAsync(stoppingToken);
        await foreach (var request in queue.ReadAllAsync(stoppingToken))
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetServices<ICachePrewarmTarget>()
                    .FirstOrDefault(candidate => candidate.Kind == request.Kind);
                if (handler is null || !await handler.PrewarmAsync(request, stoppingToken))
                {
                    CacheMetrics.PrewarmOperations
                        .WithLabels(CachePrewarmQueue.Name(request.Kind), "error").Inc();
                    continue;
                }
                CacheMetrics.PrewarmOperations
                    .WithLabels(CachePrewarmQueue.Name(request.Kind), "success").Inc();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                CacheMetrics.PrewarmOperations
                    .WithLabels(CachePrewarmQueue.Name(request.Kind), "error").Inc();
                logger.LogWarning(
                    "Optional cache prewarm failed for {Kind} ({ErrorType}).",
                    CachePrewarmQueue.Name(request.Kind),
                    error.GetType().Name);
            }
            finally
            {
                CacheMetrics.PrewarmDuration.WithLabels(CachePrewarmQueue.Name(request.Kind))
                    .Observe(Stopwatch.GetElapsedTime(started).TotalSeconds);
            }
        }
    }

    private async Task QueueStartupAsync(CancellationToken cancellationToken)
    {
        if (_options.PrewarmStartupDelayMilliseconds > 0)
        {
            await Task.Delay(_options.PrewarmStartupDelayMilliseconds, cancellationToken);
        }
        foreach (var rawStudioId in _options.PrewarmStartupStudioIds)
        {
            if (!Guid.TryParse(rawStudioId, out var studioId))
            {
                continue;
            }
            await QueueStartupKindAsync(CachePrewarmKind.StudioSettings, "studio", studioId, cancellationToken);
            await QueueStartupKindAsync(CachePrewarmKind.TaskReferences, "task-references", studioId, cancellationToken);
        }
    }

    private async Task QueueStartupKindAsync(
        CachePrewarmKind kind,
        string domain,
        Guid studioId,
        CancellationToken cancellationToken)
    {
        var generation = await generations.GetAsync(domain, $"studio-{studioId:N}", cancellationToken);
        if (generation is not null)
        {
            queue.TryQueue(new CachePrewarmRequest(kind, studioId, generation));
        }
    }
}
