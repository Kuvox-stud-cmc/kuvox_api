using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Kuvox.Api.Modules.Shared.Infrastructure.Caching;

public interface IRedisConnectionProvider
{
    Task<IDatabase> GetDatabaseAsync(CancellationToken cancellationToken = default);
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}

public sealed class RedisConnectionProvider : IRedisConnectionProvider, IAsyncDisposable
{
    private readonly IConfiguration _configuration;
    private readonly CachingOptions _options;
    private readonly ILogger<RedisConnectionProvider> _logger;
    private readonly object _gate = new();
    private Task<IConnectionMultiplexer>? _connectionTask;

    public RedisConnectionProvider(
        IConfiguration configuration,
        IOptions<CachingOptions> options,
        ILogger<RedisConnectionProvider> logger)
    {
        _configuration = configuration;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IDatabase> GetDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);
        return connection.GetDatabase();
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return true;
        }

        try
        {
            var database = await GetDatabaseAsync(cancellationToken);
            var result = await database.PingAsync().WaitAsync(
                TimeSpan.FromMilliseconds(_options.OperationTimeoutMilliseconds),
                cancellationToken);
            return result >= TimeSpan.Zero;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            _logger.LogWarning("Redis health check failed ({ErrorType}).", error.GetType().Name);
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task<IConnectionMultiplexer>? task;
        lock (_gate)
        {
            task = _connectionTask;
        }

        if (task is { IsCompletedSuccessfully: true })
        {
            await task.Result.CloseAsync();
            task.Result.Dispose();
        }
    }

    private async Task<IConnectionMultiplexer> GetConnectionAsync(CancellationToken cancellationToken)
    {
        Task<IConnectionMultiplexer> task;
        lock (_gate)
        {
            task = _connectionTask ??= ConnectAsync();
        }

        try
        {
            return await task.WaitAsync(
                TimeSpan.FromMilliseconds(_options.ConnectTimeoutMilliseconds),
                cancellationToken);
        }
        catch
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_connectionTask, task))
                    {
                        _connectionTask = null;
                    }
                }
            }
            throw;
        }
    }

    private async Task<IConnectionMultiplexer> ConnectAsync()
    {
        var connectionString = _configuration.GetConnectionString("Redis") ?? "localhost:6379";
        var configuration = ConfigurationOptions.Parse(connectionString);
        configuration.AbortOnConnectFail = false;
        configuration.ConnectRetry = 0;
        configuration.ConnectTimeout = _options.ConnectTimeoutMilliseconds;
        configuration.AsyncTimeout = _options.OperationTimeoutMilliseconds;
        configuration.SyncTimeout = _options.OperationTimeoutMilliseconds;
        var endpoints = string.Join(',', configuration.EndPoints.Select(endpoint => endpoint.ToString()));
        _logger.LogInformation("Configuring optional Redis cache endpoint {Endpoints}.", endpoints);
        return await ConnectionMultiplexer.ConnectAsync(configuration);
    }
}
