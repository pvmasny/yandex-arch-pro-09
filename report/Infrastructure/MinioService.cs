using Minio;
using Minio.DataModel.Args;

namespace report.Infrastructure;

public class MinioService : IMinioService
{
    private readonly IMinioClient _minioClient;
    private readonly string _bucketName;
    private readonly string _cdnEndpoint;
    private readonly ILogger<MinioService> _logger;
    public MinioService(IConfiguration configuration, ILogger<MinioService> logger)
    {
        _logger = logger;
        _bucketName = configuration["Minio:BucketName"] ?? "reports";
        _cdnEndpoint = configuration["Cdn:Endpoint"] ?? "http://localhost:8082";

        var endpoint = configuration["Minio:Endpoint"] ?? "minio:9000";
        var accessKey = configuration["Minio:AccessKey"] ?? "minio_user";
        var secretKey = configuration["Minio:SecretKey"] ?? "minio_password";

        _minioClient = new MinioClient()
            .WithEndpoint(endpoint)
            .WithCredentials(accessKey, secretKey)
            .WithSSL(false)
            .Build();
    }
    public async Task<bool> IsExistsAsync(string userId, string reportKey)
    {
        try
        {
            var args = new StatObjectArgs()
                .WithBucket(_bucketName)
                .WithObject($"{userId}/{reportKey}");

            var stat = await _minioClient.StatObjectAsync(args);
            return stat != null;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> GetReportUrlAsync(string userId, string reportKey)
    {
        // Возвращаем URL через CDN
        return $"{_cdnEndpoint}/{_bucketName}/{userId}/{reportKey}";
    }

    public async Task SaveReportAsync(string userId, string reportKey, object reportData, string format = "json")
    {
        try
        {
            string content;
            string contentType;

            if (format == "csv")
            {
                content = reportData.ToString() ?? string.Empty;
                contentType = "text/csv";
            }
            else
            {
                content = System.Text.Json.JsonSerializer.Serialize(reportData);
                contentType = "application/json";
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(content);
            using var stream = new MemoryStream(bytes);

            var putArgs = new PutObjectArgs()
                .WithBucket(_bucketName)
                .WithObject($"{userId}/{reportKey}")
                .WithStreamData(stream)
                .WithObjectSize(bytes.Length)
                .WithContentType(contentType);

            await _minioClient.PutObjectAsync(putArgs);

            _logger.LogInformation("Report saved: {UserId}/{ReportKey}", userId, reportKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save report for user {UserId}: {ReportKey}", userId, reportKey);
            throw;
        }
    }

    public async Task<string> GeneratePresignedUrlAsync(string userId, string reportKey, int expiryInMinutes = 60)
    {
        var args = new PresignedGetObjectArgs()
            .WithBucket(_bucketName)
            .WithObject($"{userId}/{reportKey}")
            .WithExpiry(expiryInMinutes * 60);

        return await _minioClient.PresignedGetObjectAsync(args);
    }

}
