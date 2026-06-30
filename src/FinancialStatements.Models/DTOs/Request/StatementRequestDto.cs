using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using FinancialStatements.Models.Enums;

namespace FinancialStatements.Models.DTOs.Request;

public class StatementRequestDto
{
    [JsonPropertyName("accountId")]
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string AccountId { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    [Required]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StatementType Type { get; set; }

    [JsonPropertyName("from")]
    [Required]
    public DateTimeOffset From { get; set; }

    [JsonPropertyName("to")]
    [Required]
    public DateTimeOffset To { get; set; }
}
