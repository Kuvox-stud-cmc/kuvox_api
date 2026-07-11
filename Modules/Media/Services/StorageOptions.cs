using Microsoft.Extensions.Options;

namespace Kuvox.Api.Modules.Media.Services;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string Endpoint { get; init; } = string.Empty;
    public string Region { get; init; } = "us-east-1";
    public string AccessKey { get; init; } = string.Empty;
    public string SecretKey { get; init; } = string.Empty;
    public string RawBucketName { get; init; } = "kuvox-raw";
    public string RenderBucketName { get; init; } = "kuvox-renders";
    public bool CreateBucket { get; init; }

    public static string? Validate(StorageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            return "Storage configuration error: Storage:Endpoint is required.";
        }

        if (string.IsNullOrWhiteSpace(options.Region))
        {
            return "Storage configuration error: Storage:Region is required.";
        }

        var hasAccessKey = !string.IsNullOrWhiteSpace(options.AccessKey);
        var hasSecretKey = !string.IsNullOrWhiteSpace(options.SecretKey);

        if (!hasAccessKey && !hasSecretKey)
        {
            return "Storage configuration error: Storage:AccessKey and Storage:SecretKey are required.";
        }

        if (!hasAccessKey)
        {
            return "Storage configuration error: Storage:AccessKey is required when Storage:SecretKey is set.";
        }

        if (!hasSecretKey)
        {
            return "Storage configuration error: Storage:SecretKey is required when Storage:AccessKey is set.";
        }

        if (string.IsNullOrWhiteSpace(options.RawBucketName))
        {
            return "Storage configuration error: Storage:RawBucketName is required.";
        }

        if (string.IsNullOrWhiteSpace(options.RenderBucketName))
        {
            return "Storage configuration error: Storage:RenderBucketName is required.";
        }

        return null;
    }
}

public sealed class StorageOptionsValidator : IValidateOptions<StorageOptions>
{
    public ValidateOptionsResult Validate(string? name, StorageOptions options)
    {
        var error = StorageOptions.Validate(options);
        return error is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(error);
    }
}
