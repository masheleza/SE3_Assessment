using Amazon.S3;
using Amazon.S3.Model;
using FinancialStatements.Models.Events;

namespace FinancialStatements.DocumentApi.Services;

public interface IDocumentStorageService
{
    Task<byte[]> GenerateStatementAsync(StatementRequestedEvent request, CancellationToken ct = default);
    Task<string> UploadAsync(string documentId, byte[] content, CancellationToken ct = default);
    Task<byte[]?> DownloadAsync(string storagePath, CancellationToken ct = default);
}

public sealed class S3DocumentStorageService : IDocumentStorageService
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucketName;
    private readonly ILogger<S3DocumentStorageService> _logger;

    public S3DocumentStorageService(IAmazonS3 s3, IConfiguration config, ILogger<S3DocumentStorageService> logger)
    {
        _s3 = s3;
        _bucketName = config["Storage:BucketName"]
            ?? throw new InvalidOperationException("Storage:BucketName is not configured.");
        _logger = logger;
    }

    public Task<byte[]> GenerateStatementAsync(StatementRequestedEvent request, CancellationToken ct = default)
    {
        // Production: integrate iTextSharp / QuestPDF / SSRS for real PDF generation.
        // This stub returns a minimal PDF placeholder for the architecture scaffold.
        var content = $"""
            FINANCIAL STATEMENT
            -------------------
            User: {request.UserId}
            Account: {request.AccountId}
            Type: {request.Type}
            Period: {request.From:yyyy-MM-dd} to {request.To:yyyy-MM-dd}
            Generated: {DateTimeOffset.UtcNow:O}
            Document ID: {request.DocumentId}
            """;

        return Task.FromResult(System.Text.Encoding.UTF8.GetBytes(content));
    }

    public async Task<string> UploadAsync(string documentId, byte[] content, CancellationToken ct = default)
    {
        var key = $"statements/{DateTimeOffset.UtcNow:yyyy/MM}/{documentId}.pdf";

        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            InputStream = new MemoryStream(content),
            ContentType = "application/pdf",
            ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
        }, ct);

        _logger.LogInformation("Uploaded document {DocumentId} to s3://{Bucket}/{Key}", documentId, _bucketName, key);
        return key;
    }

    public async Task<byte[]?> DownloadAsync(string storagePath, CancellationToken ct = default)
    {
        try
        {
            var response = await _s3.GetObjectAsync(_bucketName, storagePath, ct);
            using var ms = new MemoryStream();
            await response.ResponseStream.CopyToAsync(ms, ct);
            return ms.ToArray();
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Document not found in S3 at path {Path}", storagePath);
            return null;
        }
    }
}
