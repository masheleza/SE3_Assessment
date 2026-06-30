using System.Text.Json.Serialization;
using FinancialStatements.Models.Enums;

namespace FinancialStatements.Models.Events;

public class StatementRequestedEvent
{
    [JsonPropertyName("correlationId")]
    public string CorrelationId { get; set; } = string.Empty;

    [JsonPropertyName("documentId")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("accountId")]
    public string AccountId { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StatementType Type { get; set; }

    [JsonPropertyName("from")]
    public DateTimeOffset From { get; set; }

    [JsonPropertyName("to")]
    public DateTimeOffset To { get; set; }

    [JsonPropertyName("responseQueueUrl")]
    public string ResponseQueueUrl { get; set; } = string.Empty;

    [JsonPropertyName("requestedAt")]
    public DateTimeOffset RequestedAt { get; set; }
}
