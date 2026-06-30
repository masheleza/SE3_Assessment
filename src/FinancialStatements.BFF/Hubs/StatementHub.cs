using FinancialStatements.Models.DTOs.Response;
using FinancialStatements.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FinancialStatements.BFF.Hubs;

public interface IStatementHubClient
{
    Task ReceiveMessage(ChatMessageDto message, CancellationToken ct = default);
    Task LinkExpiring(string token, int minutesRemaining, CancellationToken ct = default);
    Task ConnectionEstablished(string connectionId, CancellationToken ct = default);
}

[Authorize]
public sealed class StatementHub : Hub<IStatementHubClient>
{
    private readonly ILogger<StatementHub> _logger;

    public StatementHub(ILogger<StatementHub> logger) => _logger = logger;

    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier ?? Context.ConnectionId;
        _logger.LogInformation("Client connected. UserId={UserId}, ConnectionId={ConnectionId}", userId, Context.ConnectionId);

        await Clients.Caller.ConnectionEstablished(Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation(
            "Client disconnected. ConnectionId={ConnectionId}, Reason={Reason}",
            Context.ConnectionId, exception?.Message ?? "clean");
        return base.OnDisconnectedAsync(exception);
    }

    public async Task SendMessage(string content)
    {
        var userId = Context.UserIdentifier ?? Context.ConnectionId;

        var message = new ChatMessageDto
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            Content = content,
            Type = MessageType.Text,
            SecureLink = null,
            SentAt = DateTimeOffset.UtcNow
        };

        await Clients.Caller.ReceiveMessage(message);
    }
}
