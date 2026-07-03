using Amazon.S3;
using Amazon.S3.Model;
using Kuvox.Api.Modules.Shared.Infrastructure;
using Microsoft.Extensions.Options;

namespace Kuvox.Api.Modules.Media.Services;

public class SeaweedFileStorageService(
  IAmazonS3 s3,
  IOptions<StorageOptions> options,
  ILogger<SeaweedFileStorageService> logger) : IFileStorageService
{
  private readonly IAmazonS3 _s3 = s3;
  private readonly StorageOptions _options = options.Value;
  private readonly ILogger<SeaweedFileStorageService> _logger = logger;

  public async Task<StoredMediaObject> UploadRawAsync(
    IFormFile file,
    Guid mediaId,
    CancellationToken cancellationToken = default
  )
  {
    if(file is null || file.Length == 0)
    {
      throw new ArgumentException("File is empty.", nameof(file));
    }

    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
    var objectKey = $"media/{mediaId}/raw/original{extension}";

    try
    {
      await EnsureBucketExistsAsync(_options.RawBucketName, cancellationToken);

      await using var stream = file.OpenReadStream();

      var request = new PutObjectRequest
      {
        BucketName = _options.RawBucketName,
        Key = objectKey,
        InputStream = stream,
        ContentType = file.ContentType,
      };
      request.Headers.ContentLength = file.Length;

      await _s3.PutObjectAsync(request, cancellationToken);
    }
    catch (Exception ex) when (IsStorageUnavailable(ex))
    {
      _logger.LogError(
        ex,
        "[Media] Raw upload storage write failed for bucket {BucketName}, key {ObjectKey}.",
        _options.RawBucketName,
        objectKey);
      throw DomainException.ServiceUnavailable(
        "Object storage is unavailable. Check the SeaweedFS S3 gateway and bucket configuration.");
    }

    return new StoredMediaObject(
      _options.RawBucketName,
      objectKey,
      file.ContentType,
      file.Length
    );
  }

  private static bool IsStorageUnavailable(Exception exception) =>
    exception is Amazon.Runtime.AmazonServiceException
      or HttpRequestException
      or IOException
      or TaskCanceledException
    || exception.InnerException is not null && IsStorageUnavailable(exception.InnerException);

  public async Task<DownloadedMediaObject> DownloadAsync(
    string bucketName,
    string objectKey,
    CancellationToken cancellationToken = default  
  )
  {
    var response = await _s3.GetObjectAsync(
      bucketName,
      objectKey,
      cancellationToken
    );

    return new DownloadedMediaObject(
      response.ResponseStream,
      response.Headers.ContentType,
      response.Headers.ContentLength,
      response.ETag);
  }

  public async Task DeleteAsync(
    string bucketName,
    string objectKey,
    CancellationToken cancellationToken = default
  )
  {
    await _s3.DeleteObjectAsync(
      bucketName, 
      objectKey, 
      cancellationToken
    );
  }

  private async Task EnsureBucketExistsAsync(
    string bucketName,
    CancellationToken cancellationToken = default
  )
  {
    if (!_options.CreateBucket)
    {
      return;
    }

    var buckets = await _s3.ListBucketsAsync(cancellationToken);

    var exists = buckets.Buckets.Any(b => b.BucketName == bucketName);

    if (!exists)
    {
      await _s3.PutBucketAsync(new PutBucketRequest
      {
        BucketName = bucketName,
      }, cancellationToken);
    }
  }
}
