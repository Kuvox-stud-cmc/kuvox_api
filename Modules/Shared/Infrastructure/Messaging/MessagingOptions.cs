namespace Kuvox.Api.Modules.Shared.Infrastructure.Messaging;

public sealed class MessagingOptions
{
    public int[] RetryDelaysSeconds { get; init; } = [30, 120, 600];
    public int ConsumerPrefetch { get; init; } = 4;
    public int OutboxPollIntervalSeconds { get; init; } = 5;
    public int OutboxBatchSize { get; init; } = 50;

    public TimeSpan RetryDelayForAttempt(int attemptCount)
    {
        var index = Math.Clamp(attemptCount - 1, 0, RetryDelaysSeconds.Length - 1);
        return TimeSpan.FromSeconds(RetryDelaysSeconds[index]);
    }

    public int MaxAttempts => RetryDelaysSeconds.Length;
}
