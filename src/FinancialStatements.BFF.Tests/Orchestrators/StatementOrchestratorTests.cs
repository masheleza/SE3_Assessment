namespace FinancialStatements.BFF.Tests.Orchestrators;

public sealed class StatementOrchestratorTests
{
    private readonly Mock<IStatementDelegate> _monthlyDelegate = new();
    private readonly Mock<IStatementDelegate> _annualDelegate = new();
    private readonly Mock<ISecureLinkService> _secureLinkService = new();
    private readonly Mock<ILogger<StatementOrchestrator>> _logger = new();
    private readonly StatementOrchestrator _sut;

    private static readonly StatementRequest MonthlyRequest = new(
        UserId: "user-1",
        AccountId: "acc-1",
        Type: StatementType.Monthly,
        From: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        To: new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero),
        ConnectionId: "conn-1");

    public StatementOrchestratorTests()
    {
        _monthlyDelegate.Setup(d => d.CanHandle(StatementType.Monthly)).Returns(true);
        _annualDelegate.Setup(d => d.CanHandle(StatementType.Annual)).Returns(true);

        _sut = new StatementOrchestrator(
            [_monthlyDelegate.Object, _annualDelegate.Object],
            _secureLinkService.Object,
            _logger.Object);
    }

    [Fact]
    public async Task RequestStatementAsync_DispatchesToMatchingDelegate()
    {
        var documentId = Guid.NewGuid().ToString();
        _monthlyDelegate
            .Setup(d => d.ExecuteAsync(MonthlyRequest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StatementResult(true, documentId, null));
        _secureLinkService
            .Setup(s => s.GenerateAsync(documentId, MonthlyRequest.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildLink(documentId));

        await _sut.RequestStatementAsync(MonthlyRequest);

        _monthlyDelegate.Verify(d => d.ExecuteAsync(MonthlyRequest, It.IsAny<CancellationToken>()), Times.Once);
        _annualDelegate.Verify(d => d.ExecuteAsync(It.IsAny<StatementRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RequestStatementAsync_ReturnsLinkFromSecureLinkService()
    {
        var documentId = Guid.NewGuid().ToString();
        var expected = BuildLink(documentId);

        _monthlyDelegate
            .Setup(d => d.ExecuteAsync(MonthlyRequest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StatementResult(true, documentId, null));
        _secureLinkService
            .Setup(s => s.GenerateAsync(documentId, MonthlyRequest.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _sut.RequestStatementAsync(MonthlyRequest);

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task RequestStatementAsync_WhenNoDelegateFound_ThrowsInvalidOperationException()
    {
        var request = MonthlyRequest with { Type = StatementType.Transaction };

        await _sut.Invoking(s => s.RequestStatementAsync(request))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Transaction*");
    }

    [Fact]
    public async Task RequestStatementAsync_WhenDelegateReturnsFailure_Throws()
    {
        _monthlyDelegate
            .Setup(d => d.ExecuteAsync(MonthlyRequest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StatementResult(false, null, "storage unavailable"));

        await _sut.Invoking(s => s.RequestStatementAsync(MonthlyRequest))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*storage unavailable*");
    }

    [Fact]
    public async Task RequestStatementAsync_WhenDelegateReturnsNullDocumentId_Throws()
    {
        _monthlyDelegate
            .Setup(d => d.ExecuteAsync(MonthlyRequest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StatementResult(true, null, null));

        await _sut.Invoking(s => s.RequestStatementAsync(MonthlyRequest))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RequestStatementAsync_PassesDocumentIdAndUserIdToSecureLinkService()
    {
        var documentId = "doc-xyz";
        _monthlyDelegate
            .Setup(d => d.ExecuteAsync(MonthlyRequest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StatementResult(true, documentId, null));
        _secureLinkService
            .Setup(s => s.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildLink(documentId));

        await _sut.RequestStatementAsync(MonthlyRequest);

        _secureLinkService.Verify(
            s => s.GenerateAsync(documentId, MonthlyRequest.UserId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static SecureLinkResponseDto BuildLink(string documentId) => new()
    {
        LinkUrl = $"https://localhost/download/token",
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
        DocumentId = documentId
    };
}
