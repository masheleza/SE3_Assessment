namespace FinancialStatements.BFF.Tests.Delegates;

public sealed class TransactionStatementDelegateTests
{
    private readonly Mock<ISqsPublisher> _publisher = new();
    private readonly Mock<ILogger<TransactionStatementDelegate>> _logger = new();
    private readonly TransactionStatementDelegate _sut;

    private static readonly StatementRequest Request = new(
        UserId: "user-1",
        AccountId: "acc-1",
        Type: StatementType.Transaction,
        From: new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
        To: new DateTimeOffset(2026, 4, 30, 0, 0, 0, TimeSpan.Zero),
        ConnectionId: "conn-1");

    public TransactionStatementDelegateTests()
    {
        _publisher.Setup(p => p.ResponseQueueUrl).Returns("https://sqs/response");
        _publisher.Setup(p => p.PublishAsync(It.IsAny<StatementRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _sut = new TransactionStatementDelegate(_publisher.Object, _logger.Object);
    }

    [Fact]
    public void CanHandle_Transaction_ReturnsTrue() =>
        _sut.CanHandle(StatementType.Transaction).Should().BeTrue();

    [Theory]
    [InlineData(StatementType.Monthly)]
    [InlineData(StatementType.Annual)]
    public void CanHandle_OtherTypes_ReturnsFalse(StatementType type) =>
        _sut.CanHandle(type).Should().BeFalse();

    [Fact]
    public async Task ExecuteAsync_PreservesExactDateRange()
    {
        StatementRequestedEvent? published = null;
        _publisher
            .Setup(p => p.PublishAsync(It.IsAny<StatementRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<StatementRequestedEvent, CancellationToken>((e, _) => published = e)
            .Returns(Task.CompletedTask);

        await _sut.ExecuteAsync(Request);

        published!.From.Should().Be(Request.From);
        published.To.Should().Be(Request.To);
    }

    [Fact]
    public async Task ExecuteAsync_PublishedEventHasTransactionType()
    {
        StatementRequestedEvent? published = null;
        _publisher
            .Setup(p => p.PublishAsync(It.IsAny<StatementRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<StatementRequestedEvent, CancellationToken>((e, _) => published = e)
            .Returns(Task.CompletedTask);

        await _sut.ExecuteAsync(Request);

        published!.Type.Should().Be(StatementType.Transaction);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsSuccessWithDocumentId()
    {
        var result = await _sut.ExecuteAsync(Request);

        result.Success.Should().BeTrue();
        result.DocumentId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_EachCallGeneratesUniqueDocumentId()
    {
        var first = await _sut.ExecuteAsync(Request);
        var second = await _sut.ExecuteAsync(Request);

        first.DocumentId.Should().NotBe(second.DocumentId);
    }
}
