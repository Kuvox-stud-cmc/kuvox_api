namespace Kuvox.Api.Modules.Media.Services;

public sealed class MediaPipelineRecoveryOptions
{
    public const string SectionName = "MediaPipelineRecovery";

    public bool Enabled { get; init; } = true;
    public int StaleAfterMinutes { get; init; } = 15;
    public int PollIntervalSeconds { get; init; } = 60;
    public int BatchSize { get; init; } = 100;
}
