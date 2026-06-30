namespace FinancialStatements.BFF.Tests.Controllers;

public sealed class DocumentControllerTests
{
    private readonly Mock<ISecureLinkService> _secureLinkService = new();
    private readonly Mock<IDocumentProxyService> _documentProxy = new();
    private readonly Mock<ILogger<DocumentController>> _logger = new();
    private readonly DocumentController _sut;

    public DocumentControllerTests() =>
        _sut = new DocumentController(_secureLinkService.Object, _documentProxy.Object, _logger.Object);

    [Fact]
    public async Task Download_WhenTokenNotFound_Returns404()
    {
        _secureLinkService.Setup(s => s.ValidateAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SecureLink?)null);

        var result = await _sut.Download("missing", CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Download_WhenTokenAlreadyUsed_Returns410()
    {
        var usedLink = new SecureLink("tok", "doc-1", "user-1", DateTimeOffset.UtcNow.AddMinutes(25), IsUsed: true);
        _secureLinkService.Setup(s => s.ValidateAsync("tok", It.IsAny<CancellationToken>()))
            .ReturnsAsync(usedLink);

        var result = await _sut.Download("tok", CancellationToken.None);

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status410Gone);
    }

    [Fact]
    public async Task Download_WhenTokenExpired_Returns410()
    {
        var expiredLink = new SecureLink("tok", "doc-1", "user-1", DateTimeOffset.UtcNow.AddMinutes(-1));
        _secureLinkService.Setup(s => s.ValidateAsync("tok", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expiredLink);

        var result = await _sut.Download("tok", CancellationToken.None);

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status410Gone);
    }

    [Fact]
    public async Task Download_ValidToken_Returns200WithFileContent()
    {
        var validLink = new SecureLink("tok", "doc-1", "user-1", DateTimeOffset.UtcNow.AddMinutes(25));
        _secureLinkService.Setup(s => s.ValidateAsync("tok", It.IsAny<CancellationToken>()))
            .ReturnsAsync(validLink);
        _secureLinkService.Setup(s => s.MarkUsedAsync("tok", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var pdfBytes = new byte[] { 37, 80, 68, 70 }; // %PDF
        _documentProxy.Setup(p => p.FetchDocumentAsync("doc-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((pdfBytes, "application/pdf", "statement-doc-1.pdf"));

        var result = await _sut.Download("tok", CancellationToken.None);

        var fileResult = result.Should().BeOfType<FileContentResult>().Subject;
        fileResult.FileContents.Should().BeEquivalentTo(pdfBytes);
        fileResult.ContentType.Should().Be("application/pdf");
    }

    [Fact]
    public async Task Download_ValidToken_MarksTokenUsed()
    {
        var validLink = new SecureLink("tok", "doc-1", "user-1", DateTimeOffset.UtcNow.AddMinutes(25));
        _secureLinkService.Setup(s => s.ValidateAsync("tok", It.IsAny<CancellationToken>()))
            .ReturnsAsync(validLink);
        _secureLinkService.Setup(s => s.MarkUsedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _documentProxy.Setup(p => p.FetchDocumentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new byte[1], "application/pdf", "file.pdf"));

        await _sut.Download("tok", CancellationToken.None);

        _secureLinkService.Verify(s => s.MarkUsedAsync("tok", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Download_ValidToken_DoesNotCallProxyBeforeMarkingUsed()
    {
        var callOrder = new List<string>();

        var validLink = new SecureLink("tok", "doc-1", "user-1", DateTimeOffset.UtcNow.AddMinutes(25));
        _secureLinkService.Setup(s => s.ValidateAsync("tok", It.IsAny<CancellationToken>()))
            .ReturnsAsync(validLink);
        _secureLinkService.Setup(s => s.MarkUsedAsync("tok", It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("mark")).Returns(Task.CompletedTask);
        _documentProxy.Setup(p => p.FetchDocumentAsync("doc-1", It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("fetch"))
            .ReturnsAsync((new byte[1], "application/pdf", "file.pdf"));

        await _sut.Download("tok", CancellationToken.None);

        callOrder.Should().ContainInOrder("mark", "fetch");
    }
}
