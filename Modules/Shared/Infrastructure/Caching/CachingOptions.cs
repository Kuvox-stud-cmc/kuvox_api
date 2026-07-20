namespace Kuvox.Api.Modules.Shared.Infrastructure.Caching;

public sealed class CachingOptions
{
    public const string SectionName = "Caching";

    public bool Enabled { get; set; }
    public string KeyPrefix { get; set; } = "kuvox:v1";
    public int TtlJitterPercent { get; set; } = 10;
    public int ConnectTimeoutMilliseconds { get; set; } = 500;
    public int OperationTimeoutMilliseconds { get; set; } = 500;
    public int MaxPayloadBytes { get; set; } = 1_048_576;
    public bool HttpValidatorsEnabled { get; set; }
    public bool StampedeProtectionEnabled { get; set; }
    public bool StudioUsageSingleFlightEnabled { get; set; }
    public int LockTtlMilliseconds { get; set; } = 5_000;
    public int LockWaitMilliseconds { get; set; } = 2_000;
    public int LockPollMilliseconds { get; set; } = 50;
    public bool StudioSettingsPrewarmEnabled { get; set; }
    public bool TaskReferencePrewarmEnabled { get; set; }
    public int PrewarmQueueCapacity { get; set; } = 256;
    public int PrewarmStartupDelayMilliseconds { get; set; } = 5_000;
    public string[] PrewarmStartupStudioIds { get; set; } = [];
    public CacheFeatureOptions BusinessReads { get; set; } = new();
    public int GenerationTtlSeconds { get; set; } = 2_592_000;
    public CacheFeatureOptions UserSettings { get; set; } = new() { TtlSeconds = 120 };
    public StudioCacheOptions Studio { get; set; } = new();
    public ProjectCacheOptions Projects { get; set; } = new();
    public CacheFeatureOptions Media { get; set; } = new() { TtlSeconds = 20 };
    public CacheFeatureOptions Albums { get; set; } = new() { TtlSeconds = 20 };
    public TaskCacheOptions Tasks { get; set; } = new();
    public CacheFeatureOptions StorageUsage { get; set; } = new() { TtlSeconds = 15 };
    public CacheFeatureOptions Notifications { get; set; } = new() { TtlSeconds = 10 };
    public CacheFeatureOptions NotificationCount { get; set; } = new() { TtlSeconds = 10 };
    public CacheFeatureOptions EditorDocuments { get; set; } = new() { TtlSeconds = 15 };
    public CacheFeatureOptions RenderJobs { get; set; } = new() { TtlSeconds = 3 };
}

public class CacheFeatureOptions
{
    public bool Enabled { get; set; }
    public int TtlSeconds { get; set; }
}

public sealed class StudioCacheOptions : CacheFeatureOptions
{
    public int SettingsTtlSeconds { get; set; } = 120;
    public int ReferencesTtlSeconds { get; set; } = 600;
}

public sealed class ProjectCacheOptions : CacheFeatureOptions
{
    public ProjectCacheOptions() => TtlSeconds = 30;

    public int DetailTtlSeconds { get; set; } = 60;
    public int ListTtlSeconds { get; set; } = 30;
    public int MediaTtlSeconds { get; set; } = 20;
}

public sealed class TaskCacheOptions : CacheFeatureOptions
{
    public TaskCacheOptions() => TtlSeconds = 15;

    public int ReferencesTtlSeconds { get; set; } = 300;
}

public sealed class MetricsOptions
{
    public const string SectionName = "Metrics";
    public bool Enabled { get; set; } = true;
}
