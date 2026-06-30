namespace FinancialStatements.BFF.Tests.Delegates;

public sealed class MonthlyStatementDelegateTests
{
    private readonly Mock<ISqsPublisher> _publisher = new();
    private readonly Mock<ILogger<MonthlyStatementDelegate>> _logger = new();
    private readonly MonthlyStatementDelegate _sut;

    private static readonly StatementRequest Request = new(
        UserId: "user-1",
        AccountId: "acc-1",
        Type: StatementType.Monthly,
        From: new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
        To: new DateTimeOffset(2026, 3, 31, 0, 0, 0, TimeSpan.Zero),
        ConnectionId: "conn-1");

    public MonthlyStatementDelegateTests()
    {
        _publisher.Setup(p => p.ResponseQueueUrl).Returns("https://sqs/response");
        _publisher.Setup(p => p.PublishAsync(It.IsAny<StatementRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _sut = new MonthlyStatementDelegate(_publisher.Object, _logger.Object);
    }

    [Fact]
    public void CanHandle_Monthly_ReturnsTrue() =>
        _sut.CanHandle(StatementType.Monthly).Should().BeTrue();

    [Theory]
    [InlineData(StatementType.Annual)]
    [InlineData(StatementType.Transaction)]
    public void CanHandle_OtherTypes_ReturnsFalse(StatementType type) =>
        _sut.CanHandle(type).Should().BeFalse();

    [Fact]
    public async Task ExecuteAsync_ReturnsSuccess()
    {
        var result = await _sut.ExecuteAsync(Request);

        result.Success.Should().BeTrue();
        result.DocumentId.Should().NotBeNullOrEmpty();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_PublishesStatementRequestedEventToSqs()
    {
        await _sut.ExecuteAsync(Request);

        _publisher.Verify(
            p => p.PublishAsync(It.IsAny<StatementRequestedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_PublishedEventHasMonthlyType()
    {
        StatementRequestedEvent? published = null;
        _publisher
            .Setup(p => p.PublishAsync(It.IsAny<StatementRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<StatementRequestedEvent, CancellationToken>((e, _) => published = e)
            .Returns(Task.CompletedTask);

        await _sut.ExecuteAsync(Request);

        published!.Type.Should().Be(StatementType.Monthly);
    }

    [Fact]
    public async Task ExecuteAsync_PublishedEventPreservesRequestDates()
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
    public async Task ExecuteAsync_PublishedEventUsesResponseQueueUrl()
    {
        StatementRequestedEvent? published = null;
        _publisher
            .Setup(p => p.PublishAsync(It.IsAny<StatementRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<StatementRequestedEvent, CancellationToken>((e, _) => published = e)
            .Returns(Task.CompletedTask);

        await _sut.ExecuteAsync(Request);

        published!.ResponseQueueUrl.Should().Be("https://sqs/response");
    }
}
