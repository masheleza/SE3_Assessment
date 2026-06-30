using FinancialStatements.BFF.Infrastructure;
using FinancialStatements.BFF.Models;
using FinancialStatements.Models.Enums;
using FinancialStatements.Models.Events;

namespace FinancialStatements.BFF.Delegates;

public sealed class MonthlyStatementDelegate : IStatementDelegate
{
    private readonly ISqsPublisher _sqsPublisher;
    private readonly ILogger<MonthlyStatementDelegate> _logger;

    public MonthlyStatementDelegate(ISqsPublisher sqsPublisher, ILogger<MonthlyStatementDelegate> logger)
    {
        _sqsPublisher = sqsPublisher;
        _logger = logger;
    }

    public bool CanHandle(StatementType type) => type == StatementType.Monthly;

    public async Task<StatementResult> ExecuteAsync(StatementRequest request, CancellationToken ct = default)
    {
        var documentId = Guid.NewGuid().ToString("N");

        var @event = new StatementRequestedEvent
        {
            CorrelationId = Guid.NewGuid().ToString(),
            DocumentId = documentId,
            UserId = request.UserId,
            AccountId = request.AccountId,
            Type = StatementType.Monthly,
            From = request.From,
            To = request.To,
            ResponseQueueUrl = _sqsPublisher.ResponseQueueUrl,
            RequestedAt = DateTimeOffset.UtcNow
        };

        await _sqsPublisher.PublishAsync(@event, ct);
        _logger.LogInformation("Monthly statement requested. DocumentId={DocumentId}", documentId);

        return new StatementResult(Success: true, DocumentId: documentId, ErrorMessage: null);
    }
}
