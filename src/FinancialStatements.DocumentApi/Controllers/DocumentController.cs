using FinancialStatements.DocumentApi.Services;
using FinancialStatements.Models.DTOs.Response;
using Microsoft.AspNetCore.Mvc;

namespace FinancialStatements.DocumentApi.Controllers;

[ApiController]
[Route("api/documents")]
public sealed class DocumentController : ControllerBase
{
    private const int MaxSeedCount = 100;

    private readonly IDocumentService _documentService;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<DocumentController> _logger;

    public DocumentController(
        IDocumentService documentService,
        IHostEnvironment environment,
        ILogger<DocumentController> logger)
    {
        _documentService = documentService;
        _environment = environment;
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

    /// <summary>
    /// Test utility: seeds the store with randomly generated statements (default 10).
    /// Restricted to the Development environment.
    /// </summary>
    [HttpPost("seed")]
    public async Task<IActionResult> Seed([FromQuery] int count = 10, CancellationToken ct = default)
    {
        if (!_environment.IsDevelopment())
            return NotFound();

        if (count < 1 || count > MaxSeedCount)
            return BadRequest($"count must be between 1 and {MaxSeedCount}.");

        var created = await _documentService.SeedTestStatementsAsync(count, ct);

        var response = created
            .Select(r => new DocumentResponseDto
            {
                DocumentId = r.Id.ToString(),
                Status = r.Status,
                DownloadUrl = $"/api/documents/{r.Id}",
                CreatedAt = r.CreatedAt
            })
            .ToList();

        _logger.LogInformation("Seeded {Count} test statements via API", response.Count);

        return Ok(response);
    }
}
