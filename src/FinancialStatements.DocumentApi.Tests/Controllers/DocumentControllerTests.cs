namespace FinancialStatements.DocumentApi.Tests.Controllers;

public sealed class DocumentControllerTests
{
    private readonly Mock<IDocumentService> _documentService = new();
    private readonly Mock<ILogger<DocumentController>> _logger = new();
    private readonly DocumentController _sut;

    public DocumentControllerTests() =>
        _sut = new DocumentController(_documentService.Object, _logger.Object);

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
}
