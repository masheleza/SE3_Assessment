using System.Text.Json.Serialization;

namespace FinancialStatements.Models.DTOs.Response;

public class SecureLinkResponseDto
{
    [JsonPropertyName("linkUrl")]
    public string LinkUrl { get; set; } = string.Empty;

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset ExpiresAt { get; set; }

    [JsonPropertyName("documentId")]
    public string DocumentId { get; set; } = string.Empty;
}
