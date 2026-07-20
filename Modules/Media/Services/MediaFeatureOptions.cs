namespace Kuvox.Api.Modules.Media.Services;

public sealed class MediaFeatureOptions
{
    public bool IngestionEnabled { get; set; }

    public static MediaFeatureOptions FromConfiguration(IConfiguration configuration) =>
        new()
        {
            IngestionEnabled = string.Equals(
                configuration["KUVOX_MEDIA_INGESTION_ENABLED"]?.Trim(),
                "true",
                StringComparison.OrdinalIgnoreCase)
        };
}
