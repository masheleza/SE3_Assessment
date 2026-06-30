using FinancialStatements.BFF.Services;
using Microsoft.AspNetCore.Mvc;

namespace FinancialStatements.BFF.Controllers;

[ApiController]
[Route("api/documents")]
public sealed class DocumentController : ControllerBase
{
    private readonly ISecureLinkService _secureLinkService;
    private readonly IDocumentProxyService _documentProxy;
    private readonly ILogger<DocumentController> _logger;

    public DocumentController(
        ISecureLinkService secureLinkService,
        IDocumentProxyService documentProxy,
        ILogger<DocumentController> logger)
    {
        _secureLinkService = secureLinkService;
        _documentProxy = documentProxy;
        _logger = logger;
    }

    [HttpGet("download/{token}")]
    public async Task<IActionResult> Download(string token, CancellationToken ct)
    {
        var link = await _secureLinkService.ValidateAsync(token, ct);

        if (link is null)
            return NotFound("Link not found or has expired.");

        if (link.IsUsed)
            return Gone("This link has already been used.");

        if (link.ExpiresAt <= DateTimeOffset.UtcNow)
            return Gone("This link has expired.");

        _logger.LogInformation("Serving document {DocumentId} via secure link {Token}", link.DocumentId, token);

        await _secureLinkService.MarkUsedAsync(token, ct);

        var (content, contentType, fileName) = await _documentProxy.FetchDocumentAsync(link.DocumentId, ct);

        return File(content, contentType, fileName);
    }

    private ObjectResult Gone(string message) =>
        StatusCode(StatusCodes.Status410Gone, message);
}
