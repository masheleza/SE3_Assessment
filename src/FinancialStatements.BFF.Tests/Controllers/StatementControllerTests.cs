namespace FinancialStatements.BFF.Tests.Controllers;

public sealed class StatementControllerTests
{
    private readonly Mock<IStatementOrchestrator> _orchestrator = new();
    private readonly Mock<ILogger<StatementController>> _logger = new();
    private readonly StatementController _sut;

    private static readonly SecureLinkResponseDto LinkResponse = new()
    {
        LinkUrl = "https://localhost/download/abc",
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
        DocumentId = "doc-1"
    };

    public StatementControllerTests()
    {
        _sut = new StatementController(_orchestrator.Object, _logger.Object);
        _sut.ControllerContext = BuildContext("user-99", "conn-abc");
    }

    [Fact]
    public async Task RequestStatement_ValidRequest_Returns202Accepted()
    {
        _orchestrator
            .Setup(o => o.RequestStatementAsync(It.IsAny<StatementRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LinkResponse);

        var result = await _sut.RequestStatement(ValidDto(), CancellationToken.None);

        result.Should().BeOfType<AcceptedResult>()
            .Which.Value.Should().BeEquivalentTo(LinkResponse);
    }

    [Fact]
    public async Task RequestStatement_ExtractsUserIdFromClaims()
    {
        StatementRequest? captured = null;
        _orchestrator
            .Setup(o => o.RequestStatementAsync(It.IsAny<StatementRequest>(), It.IsAny<CancellationToken>()))
            .Callback<StatementRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(LinkResponse);

        await _sut.RequestStatement(ValidDto(), CancellationToken.None);

        captured!.UserId.Should().Be("user-99");
    }

    [Fact]
    public async Task RequestStatement_PassesSignalRConnectionIdFromHeader()
    {
        StatementRequest? captured = null;
        _orchestrator
            .Setup(o => o.RequestStatementAsync(It.IsAny<StatementRequest>(), It.IsAny<CancellationToken>()))
            .Callback<StatementRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(LinkResponse);

        await _sut.RequestStatement(ValidDto(), CancellationToken.None);

        captured!.ConnectionId.Should().Be("conn-abc");
    }

    [Fact]
    public async Task RequestStatement_MapsStatementTypeFromDto()
    {
        StatementRequest? captured = null;
        _orchestrator
            .Setup(o => o.RequestStatementAsync(It.IsAny<StatementRequest>(), It.IsAny<CancellationToken>()))
            .Callback<StatementRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(LinkResponse);

        await _sut.RequestStatement(ValidDto(StatementType.Annual), CancellationToken.None);

        captured!.Type.Should().Be(StatementType.Annual);
    }

    [Fact]
    public async Task RequestStatement_WhenUserIdentityMissing_ThrowsInvalidOperationException()
    {
        _sut.ControllerContext = BuildContext(userName: null);

        await _sut.Invoking(c => c.RequestStatement(ValidDto(), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    private static StatementRequestDto ValidDto(StatementType type = StatementType.Monthly) => new()
    {
        AccountId = "acc-1",
        Type = type,
        From = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        To = new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero)
    };

    private static ControllerContext BuildContext(string? userName = "user-99", string connectionId = "conn-abc")
    {
        var claims = userName is not null
            ? new[] { new Claim(ClaimTypes.Name, userName) }
            : Array.Empty<Claim>();

        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
        };
        http.Request.Headers["X-SignalR-ConnectionId"] = connectionId;

        return new ControllerContext { HttpContext = http };
    }
}
