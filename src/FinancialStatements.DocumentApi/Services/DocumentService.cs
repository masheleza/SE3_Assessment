using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using FinancialStatements.DocumentApi.Infrastructure.Repositories;
using FinancialStatements.DocumentApi.Models;
using FinancialStatements.Models.Enums;
using FinancialStatements.Models.Events;
using StackExchange.Redis;

namespace FinancialStatements.DocumentApi.Services;

public interface IDocumentService
{
    Task<byte[]?> GetDocumentAsync(Guid documentId, CancellationToken ct = default);
    Task ProcessRequestAsync(StatementRequestedEvent request, CancellationToken ct = default);
    Task<IReadOnlyList<DocumentRecord>> SeedTestStatementsAsync(int count, CancellationToken ct = default);
}

public sealed class DocumentService : IDocumentService
{
    private const string CachePrefix = "document:";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(2);

    private readonly IDocumentRepository _repository;
    private readonly IDocumentStorageService _storage;
    private readonly IDatabase _cache;
    private readonly IAmazonSQS _sqs;
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(
        IDocumentRepository repository,
        IDocumentStorageService storage,
        IConnectionMultiplexer redis,
        IAmazonSQS sqs,
        ILogger<DocumentService> logger)
    {
        _repository = repository;
        _storage = storage;
        _cache = redis.GetDatabase();
        _sqs = sqs;
        _logger = logger;
    }

    public async Task<byte[]?> GetDocumentAsync(Guid documentId, CancellationToken ct = default)
    {
        var cacheKey = $"{CachePrefix}{documentId}";

        // Cache-aside: check Redis first
        var cached = await _cache.StringGetAsync(cacheKey);
        if (cached.HasValue)
        {
            _logger.LogDebug("Cache hit for document {DocumentId}", documentId);
            return Convert.FromBase64String(cached.ToString());
        }

        var record = await _repository.GetByIdAsync(documentId, ct);
        if (record?.StoragePath is null || record.Status != DocumentStatus.Ready)
            return null;

        var bytes = await _storage.DownloadAsync(record.StoragePath, ct);
        if (bytes is not null)
            await _cache.StringSetAsync(cacheKey, Convert.ToBase64String(bytes), CacheTtl);

        return bytes;
    }

    public async Task ProcessRequestAsync(StatementRequestedEvent request, CancellationToken ct = default)
    {
        var documentId = Guid.Parse(request.DocumentId);

        var record = new DocumentRecord
        {
            Id = documentId,
            UserId = request.UserId,
            AccountId = request.AccountId,
            Type = request.Type,
            PeriodFrom = request.From,
            PeriodTo = request.To,
            Status = DocumentStatus.Processing
        };

        await _repository.UpsertAsync(record, ct);

        try
        {
            var pdfBytes = await _storage.GenerateStatementAsync(request, ct);
            var storagePath = await _storage.UploadAsync(documentId.ToString(), pdfBytes, ct);

            await _repository.UpdateStatusAsync(documentId, DocumentStatus.Ready, storagePath, null, ct);

            // Cache immediately after generation
            var cacheKey = $"{CachePrefix}{documentId}";
            await _cache.StringSetAsync(cacheKey, Convert.ToBase64String(pdfBytes), CacheTtl);

            await PublishResponseAsync(request, documentId.ToString(), true, null, ct);

            _logger.LogInformation("Document {DocumentId} generated and cached", documentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate document {DocumentId}", documentId);
            await _repository.UpdateStatusAsync(documentId, DocumentStatus.Failed, null, ex.Message, ct);
            await PublishResponseAsync(request, documentId.ToString(), false, ex.Message, ct);
        }
    }

    private static readonly string[] SampleUsers =
        ["user-alice", "user-bob", "user-carol", "user-dave", "user-erin"];

    public async Task<IReadOnlyList<DocumentRecord>> SeedTestStatementsAsync(int count, CancellationToken ct = default)
    {
        var statementTypes = Enum.GetValues<StatementType>();
        var created = new List<DocumentRecord>(count);

        for (var i = 0; i < count; i++)
        {
            var documentId = Guid.NewGuid();
            var type = statementTypes[Random.Shared.Next(statementTypes.Length)];

            // Random period: a window ending up to ~12 months ago.
            var periodTo = DateTimeOffset.UtcNow.AddDays(-Random.Shared.Next(0, 365));
            var periodFrom = type switch
            {
                StatementType.Annual => periodTo.AddYears(-1),
                StatementType.Monthly => periodTo.AddMonths(-1),
                _ => periodTo.AddDays(-Random.Shared.Next(1, 30))
            };

            var record = new DocumentRecord
            {
                Id = documentId,
                UserId = SampleUsers[Random.Shared.Next(SampleUsers.Length)],
                AccountId = $"acc-{Random.Shared.Next(1000, 9999)}",
                Type = type,
                PeriodFrom = periodFrom,
                PeriodTo = periodTo,
                Status = DocumentStatus.Ready,
                StoragePath = $"statements/{periodTo:yyyy/MM}/{documentId}.pdf",
                CreatedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow
            };

            await _repository.UpsertAsync(record, ct);

            // Generate stub content and cache it so the document is retrievable
            // via GetDocumentAsync without round-tripping S3 (handy for local testing).
            var stub = new StatementRequestedEvent
            {
                DocumentId = documentId.ToString(),
                UserId = record.UserId,
                AccountId = record.AccountId,
                Type = record.Type,
                From = record.PeriodFrom,
                To = record.PeriodTo
            };
            var bytes = await _storage.GenerateStatementAsync(stub, ct);
            await _cache.StringSetAsync($"{CachePrefix}{documentId}", Convert.ToBase64String(bytes), CacheTtl);

            created.Add(record);
        }

        _logger.LogInformation("Seeded {Count} test statements", created.Count);
        return created;
    }

    private async Task PublishResponseAsync(
        StatementRequestedEvent request,
        string documentId,
        bool success,
        string? error,
        CancellationToken ct)
    {
        var responseEvent = new DocumentReadyEvent
        {
            CorrelationId = request.CorrelationId,
            DocumentId = documentId,
            UserId = request.UserId,
            Success = success,
            ErrorMessage = error,
            ProcessedAt = DateTimeOffset.UtcNow
        };

        await _sqs.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = request.ResponseQueueUrl,
            MessageBody = JsonSerializer.Serialize(responseEvent)
        }, ct);
    }
}
