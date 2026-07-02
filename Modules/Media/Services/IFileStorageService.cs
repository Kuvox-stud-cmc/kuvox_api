namespace Kuvox.Api.Modules.Media.Services;

public record StoredMediaObject(
  string BucketName,
  string ObjectKey,
  string ContentType,
  long SizeBytes
);

public interface IFileStorageService
{
  Task<StoredMediaObject> UploadRawAsync(
    IFormFile file,
    Guid mediaId,
    CancellationToken cancellationToken = default
  );

  Task<Stream> DownloadAsync(
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
