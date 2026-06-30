namespace FinancialStatements.DocumentApi.Tests.Services;

public sealed class DocumentServiceTests
{
    private readonly Mock<IDocumentRepository> _repository = new();
    private readonly Mock<IDocumentStorageService> _storage = new();
    private readonly Mock<IConnectionMultiplexer> _redis = new();
    private readonly Mock<IDatabase> _db = new();
    private readonly Mock<IAmazonSQS> _sqs = new();
    private readonly Mock<ILogger<DocumentService>> _logger = new();
    private readonly DocumentService _sut;

    private static readonly StatementRequestedEvent BaseEvent = new()
    {
        CorrelationId = "corr-1",
        DocumentId = Guid.NewGuid().ToString(),
        UserId = "user-1",
        AccountId = "acc-1",
        Type = StatementType.Monthly,
        From = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        To = new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero),
        ResponseQueueUrl = "https://sqs/response",
        RequestedAt = DateTimeOffset.UtcNow
    };

    public DocumentServiceTests()
    {
        _redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_db.Object);

        _db.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        _sqs.Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Amazon.SQS.Model.SendMessageResponse { MessageId = "msg-1" });

        _sut = new DocumentService(_repository.Object, _storage.Object, _redis.Object, _sqs.Object, _logger.Object);
    }

    // ── GetDocumentAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetDocumentAsync_WhenCacheHit_ReturnsCachedBytesWithoutHittingStorage()
    {
        var pdfBytes = new byte[] { 1, 2, 3, 4 };
        _db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)Convert.ToBase64String(pdfBytes));

        var result = await _sut.GetDocumentAsync(Guid.NewGuid());

        result.Should().BeEquivalentTo(pdfBytes);
        _storage.Verify(s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetDocumentAsync_WhenCacheMiss_FetchesFromStorageAndCaches()
    {
        var documentId = Guid.NewGuid();
        var pdfBytes = new byte[] { 37, 80, 68, 70 };

        _db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        _repository.Setup(r => r.GetByIdAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentRecord
            {
                Id = documentId, Status = DocumentStatus.Ready, StoragePath = "statements/2026/01/doc.pdf"
            });

        _storage.Setup(s => s.DownloadAsync("statements/2026/01/doc.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(pdfBytes);

        var result = await _sut.GetDocumentAsync(documentId);

        result.Should().BeEquivalentTo(pdfBytes);
        _db.Verify(d => d.StringSetAsync(
            It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
            It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task GetDocumentAsync_WhenDocumentNotInDb_ReturnsNull()
    {
        _db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);
        _repository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRecord?)null);

        var result = await _sut.GetDocumentAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetDocumentAsync_WhenDocumentStatusIsNotReady_ReturnsNull()
    {
        _db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        _repository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentRecord { Status = DocumentStatus.Processing, StoragePath = "path" });

        var result = await _sut.GetDocumentAsync(Guid.NewGuid());

        result.Should().BeNull();
        _storage.Verify(s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── ProcessRequestAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task ProcessRequestAsync_OnSuccess_UpsertsRecordWithProcessingStatus()
    {
        var pdfBytes = new byte[] { 1, 2, 3 };
        _storage.Setup(s => s.GenerateStatementAsync(BaseEvent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pdfBytes);
        _storage.Setup(s => s.UploadAsync(It.IsAny<string>(), pdfBytes, It.IsAny<CancellationToken>()))
            .ReturnsAsync("statements/2026/01/doc.pdf");
        _repository.Setup(r => r.UpsertAsync(It.IsAny<DocumentRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repository.Setup(r => r.UpdateStatusAsync(It.IsAny<Guid>(), It.IsAny<DocumentStatus>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _sut.ProcessRequestAsync(BaseEvent);

        _repository.Verify(r => r.UpsertAsync(
            It.Is<DocumentRecord>(d => d.Status == DocumentStatus.Processing),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessRequestAsync_OnSuccess_UpdatesStatusToReady()
    {
        var pdfBytes = new byte[] { 1, 2, 3 };
        _storage.Setup(s => s.GenerateStatementAsync(BaseEvent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pdfBytes);
        _storage.Setup(s => s.UploadAsync(It.IsAny<string>(), pdfBytes, It.IsAny<CancellationToken>()))
            .ReturnsAsync("path/to/doc.pdf");
        _repository.Setup(r => r.UpsertAsync(It.IsAny<DocumentRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repository.Setup(r => r.UpdateStatusAsync(It.IsAny<Guid>(), It.IsAny<DocumentStatus>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _sut.ProcessRequestAsync(BaseEvent);

        _repository.Verify(r => r.UpdateStatusAsync(
            It.IsAny<Guid>(),
            DocumentStatus.Ready,
            "path/to/doc.pdf",
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessRequestAsync_OnSuccess_PublishesDocumentReadyEvent()
    {
        var pdfBytes = new byte[] { 1, 2, 3 };
        _storage.Setup(s => s.GenerateStatementAsync(BaseEvent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pdfBytes);
        _storage.Setup(s => s.UploadAsync(It.IsAny<string>(), pdfBytes, It.IsAny<CancellationToken>()))
            .ReturnsAsync("path");
        _repository.Setup(r => r.UpsertAsync(It.IsAny<DocumentRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repository.Setup(r => r.UpdateStatusAsync(It.IsAny<Guid>(), It.IsAny<DocumentStatus>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        string? publishedBody = null;
        _sqs.Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SendMessageRequest, CancellationToken>((r, _) => publishedBody = r.MessageBody)
            .ReturnsAsync(new Amazon.SQS.Model.SendMessageResponse());

        await _sut.ProcessRequestAsync(BaseEvent);

        publishedBody.Should().NotBeNull();
        var evt = JsonSerializer.Deserialize<DocumentReadyEvent>(publishedBody!);
        evt!.Success.Should().BeTrue();
        evt.UserId.Should().Be(BaseEvent.UserId);
    }

    [Fact]
    public async Task ProcessRequestAsync_WhenGenerationFails_UpdatesStatusToFailed()
    {
        _repository.Setup(r => r.UpsertAsync(It.IsAny<DocumentRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repository.Setup(r => r.UpdateStatusAsync(It.IsAny<Guid>(), It.IsAny<DocumentStatus>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _storage.Setup(s => s.GenerateStatementAsync(BaseEvent, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("generation failed"));

        await _sut.ProcessRequestAsync(BaseEvent);

        _repository.Verify(r => r.UpdateStatusAsync(
            It.IsAny<Guid>(),
            DocumentStatus.Failed,
            null,
            "generation failed",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessRequestAsync_WhenGenerationFails_PublishesFailureEvent()
    {
        _repository.Setup(r => r.UpsertAsync(It.IsAny<DocumentRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repository.Setup(r => r.UpdateStatusAsync(It.IsAny<Guid>(), It.IsAny<DocumentStatus>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _storage.Setup(s => s.GenerateStatementAsync(BaseEvent, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("disk full"));

        string? publishedBody = null;
        _sqs.Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SendMessageRequest, CancellationToken>((r, _) => publishedBody = r.MessageBody)
            .ReturnsAsync(new Amazon.SQS.Model.SendMessageResponse());

        await _sut.ProcessRequestAsync(BaseEvent);

        var evt = JsonSerializer.Deserialize<DocumentReadyEvent>(publishedBody!);
        evt!.Success.Should().BeFalse();
        evt.ErrorMessage.Should().Be("disk full");
    }
}
