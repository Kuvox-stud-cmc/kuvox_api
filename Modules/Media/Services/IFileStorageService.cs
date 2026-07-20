namespace Kuvox.Api.Modules.Media.Services;

public record StoredMediaObject(
  string BucketName,
  string ObjectKey,
  string ContentType,
  long SizeBytes
);

public sealed record DownloadedMediaObject(
  Stream Stream,
  string? ContentType,
  long? ContentLength,
  string? ETag
);

public interface IFileStorageService
{
  Task<StoredMediaObject> UploadRawAsync(
    IFormFile file,
    Guid mediaId,
    CancellationToken cancellationToken = default
  );

  Task<DownloadedMediaObject> DownloadAsync(
    string bucketName,
    string objectKey,
    CancellationToken cancellationToken = default
  );

  Task<bool> ExistsAsync(
    string bucketName,
    string objectKey,
    CancellationToken cancellationToken = default
  );

  Task DeleteAsync(
    string bucketName,
    string objectKey,
    CancellationToken cancellationToken = default
  );
}
