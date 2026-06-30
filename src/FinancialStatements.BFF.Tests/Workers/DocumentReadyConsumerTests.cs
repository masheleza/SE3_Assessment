namespace FinancialStatements.BFF.Tests.Workers;

public sealed class DocumentReadyConsumerTests
{
    private readonly Mock<IAmazonSQS> _sqs = new();
    private readonly Mock<INotificationService> _notification = new();
    private readonly Mock<ISecureLinkService> _secureLinkService = new();
    private readonly Mock<ILogger<DocumentReadyConsumer>> _logger = new();
    private readonly DocumentReadyConsumer _sut;

    private static readonly SecureLinkResponseDto LinkResponse = new()
    {
        LinkUrl = "https://test/token",
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
        DocumentId = "doc-1"
    };

    public DocumentReadyConsumerTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_notification.Object);
        services.AddSingleton(_secureLinkService.Object);
        var sp = services.BuildServiceProvider();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sqs:ResponseQueueUrl"] = "https://sqs/response"
            })
            .Build();

        _sut = new DocumentReadyConsumer(_sqs.Object, sp, config, _logger.Object);
    }

    [Fact]
    public async Task WhenSuccessEventReceived_NotifiesUserWithSecureLink()
    {
        var notified = new TaskCompletionSource<bool>();

        var @event = new DocumentReadyEvent
        {
            CorrelationId = Guid.NewGuid().ToString(),
            DocumentId = "doc-1",
            UserId = "user-1",
            Success = true,
            ProcessedAt = DateTimeOffset.UtcNow
        };

        _sqs.SetupSequence(s => s.ReceiveMessageAsync(
                It.IsAny<ReceiveMessageRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildResponse(@event))
            .ReturnsAsync(new ReceiveMessageResponse());

        _secureLinkService
            .Setup(s => s.GenerateAsync("doc-1", "user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(LinkResponse);

        _notification
            .Setup(n => n.NotifyDocumentReadyAsync(
                "user-1", "doc-1", LinkResponse, It.IsAny<CancellationToken>()))
            .Callback(() => notified.SetResult(true))
            .Returns(Task.CompletedTask);

        _sqs.Setup(s => s.DeleteMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteMessageResponse());

        await _sut.StartAsync(CancellationToken.None);
        await Task.WhenAny(notified.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        await _sut.StopAsync(CancellationToken.None);

        notified.Task.IsCompletedSuccessfully.Should().BeTrue("notification should have been triggered");
    }

    [Fact]
    public async Task WhenFailureEventReceived_NotifiesUserWithError()
    {
        var notified = new TaskCompletionSource<bool>();

        var @event = new DocumentReadyEvent
        {
            CorrelationId = Guid.NewGuid().ToString(),
            DocumentId = "doc-1",
            UserId = "user-1",
            Success = false,
            ErrorMessage = "storage error",
            ProcessedAt = DateTimeOffset.UtcNow
        };

        _sqs.SetupSequence(s => s.ReceiveMessageAsync(
                It.IsAny<ReceiveMessageRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildResponse(@event))
            .ReturnsAsync(new ReceiveMessageResponse());

        _notification
            .Setup(n => n.NotifyErrorAsync("user-1", "doc-1", "storage error", It.IsAny<CancellationToken>()))
            .Callback(() => notified.SetResult(true))
            .Returns(Task.CompletedTask);

        _sqs.Setup(s => s.DeleteMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteMessageResponse());

        await _sut.StartAsync(CancellationToken.None);
        await Task.WhenAny(notified.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        await _sut.StopAsync(CancellationToken.None);

        notified.Task.IsCompletedSuccessfully.Should().BeTrue();
        _secureLinkService.Verify(
            s => s.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task WhenMessageProcessed_DeletesFromQueue()
    {
        var deleted = new TaskCompletionSource<bool>();

        var @event = new DocumentReadyEvent
        {
            DocumentId = "doc-1", UserId = "user-1", Success = true, ProcessedAt = DateTimeOffset.UtcNow, CorrelationId = "c-1"
        };

        _sqs.SetupSequence(s => s.ReceiveMessageAsync(
                It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildResponse(@event, receiptHandle: "rcpt-99"))
            .ReturnsAsync(new ReceiveMessageResponse());

        _secureLinkService.Setup(s => s.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LinkResponse);
        _notification.Setup(n => n.NotifyDocumentReadyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SecureLinkResponseDto>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _sqs.Setup(s => s.DeleteMessageAsync("https://sqs/response", "rcpt-99", It.IsAny<CancellationToken>()))
            .Callback(() => deleted.SetResult(true))
            .ReturnsAsync(new DeleteMessageResponse());

        await _sut.StartAsync(CancellationToken.None);
        await Task.WhenAny(deleted.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        await _sut.StopAsync(CancellationToken.None);

        deleted.Task.IsCompletedSuccessfully.Should().BeTrue();
    }

    private static ReceiveMessageResponse BuildResponse(DocumentReadyEvent @event, string receiptHandle = "rcpt-1") =>
        new()
        {
            Messages =
            [
                new Message
                {
                    MessageId = Guid.NewGuid().ToString(),
                    Body = JsonSerializer.Serialize(@event),
                    ReceiptHandle = receiptHandle
                }
            ]
        };
}
