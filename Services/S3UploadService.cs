using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using DatabaseToS3Exporter.Models;

namespace DatabaseToS3Exporter.Services;

public class S3UploadService : IDisposable
{
    private readonly ExportConfiguration _config;
    private readonly ILogger<S3UploadService> _logger;
    private IAmazonS3? _s3Client;

    public S3UploadService(
        ExportConfiguration config,
        ILogger<S3UploadService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public void Initialize(S3CredentialsResponse credentials)
    {
        var awsCredentials = new SessionAWSCredentials(
            credentials.AccessKeyId,
            credentials.SecretAccessKey,
            credentials.SessionToken);

        var region = RegionEndpoint.GetBySystemName(credentials.Region);
        _s3Client = new AmazonS3Client(awsCredentials, region);

        _logger.LogInformation("Initialized S3 client for region {Region}", credentials.Region);
    }

    public async Task<string> UploadFileAsync(string filePath, string? customKey = null)
    {
        if (_s3Client == null)
            throw new InvalidOperationException("S3 client not initialized. Call Initialize first.");

        var fileName = Path.GetFileName(filePath);
        var s3Key = customKey ?? $"{_config.S3.KeyPrefix}{DateTime.UtcNow:yyyy/MM/dd}/{fileName}";

        _logger.LogInformation("Uploading {FileName} to s3://{Bucket}/{Key}",
            fileName, _config.S3.BucketName, s3Key);

        var fileInfo = new FileInfo(filePath);
        var fileSize = fileInfo.Length;

        // Use multipart upload for files larger than 100MB
        if (fileSize > 100 * 1024 * 1024)
        {
            await UploadLargeFileAsync(filePath, s3Key);
        }
        else
        {
            await UploadSmallFileAsync(filePath, s3Key);
        }

        _logger.LogInformation("Successfully uploaded {FileName} ({FileSize:N0} bytes)",
            fileName, fileSize);

        return s3Key;
    }

    private async Task UploadSmallFileAsync(string filePath, string s3Key)
    {
        var putRequest = new PutObjectRequest
        {
            BucketName = _config.S3.BucketName,
            Key = s3Key,
            FilePath = filePath,
            ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
        };

        await _s3Client!.PutObjectAsync(putRequest);
    }

    private async Task UploadLargeFileAsync(string filePath, string s3Key)
    {
        var initiateRequest = new InitiateMultipartUploadRequest
        {
            BucketName = _config.S3.BucketName,
            Key = s3Key,
            ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
        };

        var initResponse = await _s3Client!.InitiateMultipartUploadAsync(initiateRequest);
        var uploadId = initResponse.UploadId;

        try
        {
            var partSize = 10 * 1024 * 1024; // 10MB parts
            var fileInfo = new FileInfo(filePath);
            var fileSize = fileInfo.Length;
            var parts = new List<PartETag>();

            using var fileStream = File.OpenRead(filePath);
            var partNumber = 1;
            var bytesUploaded = 0L;

            while (bytesUploaded < fileSize)
            {
                var size = Math.Min(partSize, fileSize - bytesUploaded);
                var buffer = new byte[size];
                await fileStream.ReadExactlyAsync(buffer, 0, (int)size);

                using var partStream = new MemoryStream(buffer);
                var uploadRequest = new UploadPartRequest
                {
                    BucketName = _config.S3.BucketName,
                    Key = s3Key,
                    UploadId = uploadId,
                    PartNumber = partNumber,
                    InputStream = partStream
                };

                var response = await _s3Client.UploadPartAsync(uploadRequest);
                parts.Add(new PartETag(partNumber, response.ETag));

                bytesUploaded += size;
                partNumber++;

                var progress = (double)bytesUploaded / fileSize * 100;
                _logger.LogInformation("Upload progress: {Progress:F1}%", progress);
            }

            var completeRequest = new CompleteMultipartUploadRequest
            {
                BucketName = _config.S3.BucketName,
                Key = s3Key,
                UploadId = uploadId,
                PartETags = parts
            };

            await _s3Client.CompleteMultipartUploadAsync(completeRequest);
        }
        catch
        {
            await _s3Client.AbortMultipartUploadAsync(new AbortMultipartUploadRequest
            {
                BucketName = _config.S3.BucketName,
                Key = s3Key,
                UploadId = uploadId
            });
            throw;
        }
    }

    public void Dispose()
    {
        _s3Client?.Dispose();
    }
}