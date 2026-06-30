using FinancialStatements.BFF.Infrastructure;
using FinancialStatements.BFF.Models;
using FinancialStatements.Models.Enums;
using FinancialStatements.Models.Events;

namespace FinancialStatements.BFF.Delegates;

public sealed class TransactionStatementDelegate : IStatementDelegate
{
    private readonly ISqsPublisher _sqsPublisher;
    private readonly ILogger<TransactionStatementDelegate> _logger;

    public TransactionStatementDelegate(ISqsPublisher sqsPublisher, ILogger<TransactionStatementDelegate> logger)
    {
        _sqsPublisher = sqsPublisher;
        _logger = logger;
    }

    public bool CanHandle(StatementType type) => type == StatementType.Transaction;

    public async Task<StatementResult> ExecuteAsync(StatementRequest request, CancellationToken ct = default)
    {
        var documentId = Guid.NewGuid().ToString("N");

        var @event = new StatementRequestedEvent
        {
            CorrelationId = Guid.NewGuid().ToString(),
            DocumentId = documentId,
            UserId = request.UserId,
            AccountId = request.AccountId,
            Type = StatementType.Transaction,
            From = request.From,
            To = request.To,
            ResponseQueueUrl = _sqsPublisher.ResponseQueueUrl,
            RequestedAt = DateTimeOffset.UtcNow
        };

        await _sqsPublisher.PublishAsync(@event, ct);
        _logger.LogInformation("Transaction statement requested. DocumentId={DocumentId}", documentId);

        return new StatementResult(Success: true, DocumentId: documentId, ErrorMessage: null);
    }
}
