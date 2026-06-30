using System.Text.Json.Serialization;
using FinancialStatements.Models.Enums;

namespace FinancialStatements.Models.DTOs.Response;

public class ChatMessageDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MessageType Type { get; set; }

    [JsonPropertyName("secureLink")]
    public SecureLinkResponseDto? SecureLink { get; set; }

    [JsonPropertyName("sentAt")]
    public DateTimeOffset SentAt { get; set; }
}
