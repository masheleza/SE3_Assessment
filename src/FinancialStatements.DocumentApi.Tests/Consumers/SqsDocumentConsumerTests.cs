namespace FinancialStatements.DocumentApi.Tests.Consumers;

public sealed class SqsDocumentConsumerTests
{
    private readonly Mock<IAmazonSQS> _sqs = new();
    private readonly Mock<IDocumentService> _documentService = new();
    private readonly Mock<ILogger<SqsDocumentConsumer>> _logger = new();
    private readonly SqsDocumentConsumer _sut;

    private static readonly StatementRequestedEvent ValidEvent = new()
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

    public SqsDocumentConsumerTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_documentService.Object);
        var sp = services.BuildServiceProvider();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sqs:RequestQueueUrl"] = "https://sqs/requests"
            })
            .Build();

        _sut = new SqsDocumentConsumer(_sqs.Object, sp, config, _logger.Object);
    }

    [Fact]
    public async Task WhenValidMessageReceived_CallsDocumentService()
    {
        var processed = new TaskCompletionSource<bool>();

        _sqs.SetupSequence(s => s.ReceiveMessageAsync(
                It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildResponse(ValidEvent))
            .ReturnsAsync(new ReceiveMessageResponse());

        _documentService
            .Setup(d => d.ProcessRequestAsync(It.IsAny<StatementRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => processed.SetResult(true))
            .Returns(Task.CompletedTask);

        _sqs.Setup(s => s.DeleteMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteMessageResponse());

        await _sut.StartAsync(CancellationToken.None);
        await Task.WhenAny(processed.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        await _sut.StopAsync(CancellationToken.None);

        processed.Task.IsCompletedSuccessfully.Should().BeTrue("document service should have been called");
    }

    [Fact]
    public async Task WhenValidMessageProcessed_DeletesMessage()
    {
        var deleted = new TaskCompletionSource<bool>();

        _sqs.SetupSequence(s => s.ReceiveMessageAsync(
                It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildResponse(ValidEvent, receiptHandle: "rcpt-abc"))
            .ReturnsAsync(new ReceiveMessageResponse());

        _documentService
            .Setup(d => d.ProcessRequestAsync(It.IsAny<StatementRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _sqs.Setup(s => s.DeleteMessageAsync("https://sqs/requests", "rcpt-abc", It.IsAny<CancellationToken>()))
            .Callback(() => deleted.SetResult(true))
            .ReturnsAsync(new DeleteMessageResponse());

        await _sut.StartAsync(CancellationToken.None);
        await Task.WhenAny(deleted.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        await _sut.StopAsync(CancellationToken.None);

        deleted.Task.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task WhenMessageDeserializationFails_DoesNotCallDocumentService()
    {
        var pollCount = 0;
        var secondPollReached = new TaskCompletionSource<bool>();

        _sqs.SetupSequence(s => s.ReceiveMessageAsync(
                It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceiveMessageResponse
            {
                Messages = [new Message { MessageId = "m1", Body = "{{invalid-json", ReceiptHandle = "rcpt-1" }]
            })
            .ReturnsAsync(() => { secondPollReached.TrySetResult(true); return new ReceiveMessageResponse(); });

        await _sut.StartAsync(CancellationToken.None);
        await Task.WhenAny(secondPollReached.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        await _sut.StopAsync(CancellationToken.None);

        _documentService.Verify(
            d => d.ProcessRequestAsync(It.IsAny<StatementRequestedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task WhenProcessingFails_DoesNotDeleteMessage()
    {
        var pollCount = 0;
        var secondPollReached = new TaskCompletionSource<bool>();

        _sqs.SetupSequence(s => s.ReceiveMessageAsync(
                It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildResponse(ValidEvent, "rcpt-fail"))
            .ReturnsAsync(() => { secondPollReached.TrySetResult(true); return new ReceiveMessageResponse(); });

        _documentService
            .Setup(d => d.ProcessRequestAsync(It.IsAny<StatementRequestedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("unexpected crash"));

        await _sut.StartAsync(CancellationToken.None);
        await Task.WhenAny(secondPollReached.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        await _sut.StopAsync(CancellationToken.None);

        _sqs.Verify(
            s => s.DeleteMessageAsync(It.IsAny<string>(), "rcpt-fail", It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static ReceiveMessageResponse BuildResponse(StatementRequestedEvent @event, string receiptHandle = "rcpt-1") =>
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
