using FinancialStatements.DocumentApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace FinancialStatements.DocumentApi.Controllers;

[ApiController]
[Route("api/documents")]
public sealed class DocumentController : ControllerBase
{
    private readonly IDocumentService _documentService;
    private readonly ILogger<DocumentController> _logger;

    public DocumentController(IDocumentService documentService, ILogger<DocumentController> logger)
    {
        _documentService = documentService;
        _logger = logger;
    }

    [HttpGet("{documentId:guid}")]
    public async Task<IActionResult> GetDocument(Guid documentId, CancellationToken ct)
    {
        var bytes = await _documentService.GetDocumentAsync(documentId, ct);

        if (bytes is null)
            return NotFound();

        _logger.LogInformation("Serving document {DocumentId} ({Bytes} bytes)", documentId, bytes.Length);

        return File(bytes, "application/pdf", $"statement-{documentId}.pdf");
    }
}
