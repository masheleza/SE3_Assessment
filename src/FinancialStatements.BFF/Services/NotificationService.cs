using FinancialStatements.BFF.Hubs;
using FinancialStatements.Models.DTOs.Response;
using FinancialStatements.Models.Enums;
using Microsoft.AspNetCore.SignalR;

namespace FinancialStatements.BFF.Services;

public interface INotificationService
{
    Task NotifyDocumentReadyAsync(string userId, string documentId, SecureLinkResponseDto link, CancellationToken ct = default);
    Task NotifyErrorAsync(string userId, string documentId, string errorMessage, CancellationToken ct = default);
    Task NotifyLinkExpiringAsync(string userId, string token, int minutesRemaining, CancellationToken ct = default);
}

public sealed class SignalRNotificationService : INotificationService
{
    private readonly IHubContext<StatementHub, IStatementHubClient> _hub;
    private readonly ILogger<SignalRNotificationService> _logger;

    public SignalRNotificationService(
        IHubContext<StatementHub, IStatementHubClient> hub,
        ILogger<SignalRNotificationService> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task NotifyDocumentReadyAsync(string userId, string documentId, SecureLinkResponseDto link, CancellationToken ct = default)
    {
        _logger.LogInformation("Notifying user {UserId} that document {DocumentId} is ready", userId, documentId);

        var message = new ChatMessageDto
        {
            Id = Guid.NewGuid().ToString(),
            UserId = "system",
            Content = "Your financial statement is ready. Click the secure link to download — it expires in 30 minutes.",
            Type = MessageType.SecureLink,
            SecureLink = link,
            SentAt = DateTimeOffset.UtcNow
        };

        await _hub.Clients.User(userId).ReceiveMessage(message, ct);
    }

    public async Task NotifyErrorAsync(string userId, string documentId, string errorMessage, CancellationToken ct = default)
    {
        _logger.LogWarning("Notifying user {UserId} of error on document {DocumentId}: {Error}", userId, documentId, errorMessage);

        var message = new ChatMessageDto
        {
            Id = Guid.NewGuid().ToString(),
            UserId = "system",
            Content = $"Failed to generate your statement: {errorMessage}. Please try again.",
            Type = MessageType.Error,
            SecureLink = null,
            SentAt = DateTimeOffset.UtcNow
        };

        await _hub.Clients.User(userId).ReceiveMessage(message, ct);
    }

    public async Task NotifyLinkExpiringAsync(string userId, string token, int minutesRemaining, CancellationToken ct = default)
    {
        await _hub.Clients.User(userId).LinkExpiring(token, minutesRemaining, ct);
    }
}
