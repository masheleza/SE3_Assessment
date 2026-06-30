using System.Text.Json.Serialization;
using FinancialStatements.Models.Enums;

namespace FinancialStatements.Models.DTOs.Response;

public class DocumentResponseDto
{
    [JsonPropertyName("documentId")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DocumentStatus Status { get; set; }

    [JsonPropertyName("downloadUrl")]
    public string? DownloadUrl { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }
}
