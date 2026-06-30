namespace FinancialStatements.DocumentApi.Tests.Controllers;

public sealed class DocumentControllerTests
{
    private readonly Mock<IDocumentService> _documentService = new();
    private readonly Mock<IHostEnvironment> _environment = new();
    private readonly Mock<ILogger<DocumentController>> _logger = new();
    private readonly DocumentController _sut;

    public DocumentControllerTests()
    {
        _environment.SetupGet(e => e.EnvironmentName).Returns("Development");
        _sut = new DocumentController(_documentService.Object, _environment.Object, _logger.Object);
    }

    [Fact]
    public async Task GetDocument_WhenDocumentFound_Returns200WithPdfContent()
    {
        var documentId = Guid.NewGuid();
        var pdfBytes = new byte[] { 37, 80, 68, 70 }; // %PDF
        _documentService.Setup(s => s.GetDocumentAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pdfBytes);

        var result = await _sut.GetDocument(documentId, CancellationToken.None);

        var fileResult = result.Should().BeOfType<FileContentResult>().Subject;
        fileResult.FileContents.Should().BeEquivalentTo(pdfBytes);
        fileResult.ContentType.Should().Be("application/pdf");
    }

    [Fact]
    public async Task GetDocument_WhenDocumentFound_FileNameContainsDocumentId()
    {
        var documentId = Guid.NewGuid();
        _documentService.Setup(s => s.GetDocumentAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 1 });

        var result = await _sut.GetDocument(documentId, CancellationToken.None);

        result.Should().BeOfType<FileContentResult>()
            .Which.FileDownloadName.Should().Contain(documentId.ToString());
    }

    [Fact]
    public async Task GetDocument_WhenDocumentNotFound_Returns404()
    {
        _documentService.Setup(s => s.GetDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var result = await _sut.GetDocument(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetDocument_PassesCorrectDocumentIdToService()
    {
        var documentId = Guid.NewGuid();
        _documentService.Setup(s => s.GetDocumentAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 1, 2, 3 });

        await _sut.GetDocument(documentId, CancellationToken.None);

        _documentService.Verify(s => s.GetDocumentAsync(documentId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Seed ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Seed_WhenDevelopment_Returns200WithCreatedDocuments()
    {
        var created = new[] { BuildRecord(), BuildRecord() };
        _documentService.Setup(s => s.SeedTestStatementsAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(created);

        var result = await _sut.Seed(2, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dtos = ok.Value.Should().BeAssignableTo<IEnumerable<DocumentResponseDto>>().Subject;
        dtos.Should().HaveCount(2);
        dtos.Should().OnlyContain(d => d.DownloadUrl!.StartsWith("/api/documents/"));
    }

    [Fact]
    public async Task Seed_DefaultsToTenStatements()
    {
        _documentService.Setup(s => s.SeedTestStatementsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DocumentRecord>());

        await _sut.Seed(ct: CancellationToken.None);

        _documentService.Verify(s => s.SeedTestStatementsAsync(10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task Seed_WhenCountOutOfRange_Returns400(int count)
    {
        var result = await _sut.Seed(count, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        _documentService.Verify(
            s => s.SeedTestStatementsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Seed_WhenNotDevelopment_Returns404()
    {
        _environment.SetupGet(e => e.EnvironmentName).Returns("Production");

        var result = await _sut.Seed(10, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
        _documentService.Verify(
            s => s.SeedTestStatementsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static DocumentRecord BuildRecord() => new()
    {
        Id = Guid.NewGuid(),
        UserId = "user-1",
        AccountId = "acc-1",
        Type = StatementType.Monthly,
        Status = DocumentStatus.Ready,
        CreatedAt = DateTimeOffset.UtcNow
    };
}
