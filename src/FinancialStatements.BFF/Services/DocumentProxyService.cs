namespace FinancialStatements.BFF.Services;

public interface IDocumentProxyService
{
    Task<(byte[] Content, string ContentType, string FileName)> FetchDocumentAsync(string documentId, CancellationToken ct = default);
}

public sealed class DocumentProxyService : IDocumentProxyService
{
    private readonly HttpClient _http;
    private readonly ILogger<DocumentProxyService> _logger;

    public DocumentProxyService(HttpClient http, ILogger<DocumentProxyService> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<(byte[] Content, string ContentType, string FileName)> FetchDocumentAsync(
        string documentId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/documents/{documentId}", ct);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsByteArrayAsync(ct);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var fileName = response.Content.Headers.ContentDisposition?.FileName ?? $"statement-{documentId}.pdf";

        _logger.LogDebug("Fetched document {DocumentId} ({Bytes} bytes)", documentId, content.Length);

        return (content, contentType, fileName);
    }
}
