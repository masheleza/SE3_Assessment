using System.Text.Json.Serialization;

namespace FinancialStatements.Models.Events;

public class DocumentReadyEvent
{
    [JsonPropertyName("correlationId")]
    public string CorrelationId { get; set; } = string.Empty;

    [JsonPropertyName("documentId")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("processedAt")]
    public DateTimeOffset ProcessedAt { get; set; }
}
