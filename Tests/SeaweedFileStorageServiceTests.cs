using System.Reflection;
using Amazon.S3;
using Amazon.S3.Model;
using Kuvox.Api.Modules.Media.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tests;

public sealed class SeaweedFileStorageServiceTests
{
    [Fact]
    public async Task Empty_list_response_creates_raw_bucket_before_upload()
    {
        var s3 = S3Proxy.Create(out var handler);
        var service = new SeaweedFileStorageService(
            s3,
            Options.Create(new StorageOptions
            {
                RawBucketName = "kuvox-raw",
                CreateBucket = true,
            }),
            NullLogger<SeaweedFileStorageService>.Instance);
        await using var stream = new MemoryStream("video"u8.ToArray());
        var file = new FormFile(stream, 0, stream.Length, "file", "fixture.mp4")
        {
            Headers = new HeaderDictionary(),
            ContentType = "video/mp4",
        };

        await service.UploadRawAsync(file, Guid.NewGuid());

        Assert.Equal(["kuvox-raw"], handler.CreatedBuckets);
        Assert.Equal(1, handler.Uploads);
    }

    private class S3Proxy : DispatchProxy
    {
        public List<string> CreatedBuckets { get; } = [];
        public int Uploads { get; private set; }

        public static IAmazonS3 Create(out S3Proxy handler)
        {
            var client = Create<IAmazonS3, S3Proxy>();
            handler = (S3Proxy)(object)client;
            return client;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name switch
            {
                nameof(IAmazonS3.ListBucketsAsync) => Task.FromResult(
                    new ListBucketsResponse { Buckets = null! }),
                nameof(IAmazonS3.PutBucketAsync) => PutBucket((PutBucketRequest)args![0]!),
                nameof(IAmazonS3.PutObjectAsync) => PutObject(),
                _ => throw new NotSupportedException(targetMethod?.Name),
            };

        private Task<PutBucketResponse> PutBucket(PutBucketRequest request)
        {
            CreatedBuckets.Add(request.BucketName);
            return Task.FromResult(new PutBucketResponse());
        }

        private Task<PutObjectResponse> PutObject()
        {
            Uploads++;
            return Task.FromResult(new PutObjectResponse());
        }
    }
}
