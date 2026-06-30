namespace FinancialStatements.BFF.Tests.Delegates;

public sealed class AnnualStatementDelegateTests
{
    private readonly Mock<ISqsPublisher> _publisher = new();
    private readonly Mock<ILogger<AnnualStatementDelegate>> _logger = new();
    private readonly AnnualStatementDelegate _sut;

    private static readonly StatementRequest Request = new(
        UserId: "user-1",
        AccountId: "acc-1",
        Type: StatementType.Annual,
        From: new DateTimeOffset(2025, 6, 15, 0, 0, 0, TimeSpan.Zero),
        To: new DateTimeOffset(2025, 11, 20, 0, 0, 0, TimeSpan.Zero),
        ConnectionId: "conn-1");

    public AnnualStatementDelegateTests()
    {
        _publisher.Setup(p => p.ResponseQueueUrl).Returns("https://sqs/response");
        _publisher.Setup(p => p.PublishAsync(It.IsAny<StatementRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _sut = new AnnualStatementDelegate(_publisher.Object, _logger.Object);
    }

    [Fact]
    public void CanHandle_Annual_ReturnsTrue() =>
        _sut.CanHandle(StatementType.Annual).Should().BeTrue();

    [Theory]
    [InlineData(StatementType.Monthly)]
    [InlineData(StatementType.Transaction)]
    public void CanHandle_OtherTypes_ReturnsFalse(StatementType type) =>
        _sut.CanHandle(type).Should().BeFalse();

    [Fact]
    public async Task ExecuteAsync_ClampsFromToJanuaryFirst()
    {
        StatementRequestedEvent? published = null;
        _publisher
            .Setup(p => p.PublishAsync(It.IsAny<StatementRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<StatementRequestedEvent, CancellationToken>((e, _) => published = e)
            .Returns(Task.CompletedTask);

        await _sut.ExecuteAsync(Request);

        published!.From.Month.Should().Be(1);
        published.From.Day.Should().Be(1);
        published.From.Year.Should().Be(Request.From.Year);
    }

    [Fact]
    public async Task ExecuteAsync_ClampsToToDecember31()
    {
        StatementRequestedEvent? published = null;
        _publisher
            .Setup(p => p.PublishAsync(It.IsAny<StatementRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<StatementRequestedEvent, CancellationToken>((e, _) => published = e)
            .Returns(Task.CompletedTask);

        await _sut.ExecuteAsync(Request);

        published!.To.Month.Should().Be(12);
        published.To.Day.Should().Be(31);
        published.To.Year.Should().Be(Request.To.Year);
    }

    [Fact]
    public async Task ExecuteAsync_PublishedEventHasAnnualType()
    {
        StatementRequestedEvent? published = null;
        _publisher
            .Setup(p => p.PublishAsync(It.IsAny<StatementRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<StatementRequestedEvent, CancellationToken>((e, _) => published = e)
            .Returns(Task.CompletedTask);

        await _sut.ExecuteAsync(Request);

        published!.Type.Should().Be(StatementType.Annual);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsSuccessWithDocumentId()
    {
        var result = await _sut.ExecuteAsync(Request);

        result.Success.Should().BeTrue();
        result.DocumentId.Should().NotBeNullOrEmpty();
    }
}
