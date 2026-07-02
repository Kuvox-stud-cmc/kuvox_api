using Amazon.S3;
using Amazon.S3.Model;

namespace Kuvox.Api.Modules.Media.Services;

public class SeaweedFileStorageService : IFileStorageService
{
  private const string RawBucketName = "kuvox-raw";
  private readonly IAmazonS3 _s3;

  public SeaweedFileStorageService(IAmazonS3 s3)
  {
    _s3 = s3;
  }

  public async Task<StoredMediaObject> UploadRawAsync(
    IFormFile file,
    Guid projectId,
    Guid mediaId,
    CancellationToken cancellationToken = default
  )
  {
    if(file is null || file.Length == 0)
    {
      throw new ArgumentException("File is empty.", nameof(file));
    }

    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
    var objectKey = $"projects/{projectId}/media/{mediaId}/raw/original{extension}";

    await EnsureBucketExistsAsync(RawBucketName, cancellationToken);

    await using var stream = file.OpenReadStream();

    var request = new PutObjectRequest
    {
      BucketName = RawBucketName,
      Key = objectKey,
      InputStream = stream,
      ContentType = file.ContentType,
    };

    await _s3.PutObjectAsync(request, cancellationToken);

    return new StoredMediaObject(
      RawBucketName,
      objectKey,
      file.ContentType,
      file.Length
    );
  }

  public async Task<Stream> DownloadAsync(
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

    return response.ResponseStream;
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
