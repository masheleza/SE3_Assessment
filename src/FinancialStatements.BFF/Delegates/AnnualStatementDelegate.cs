using FinancialStatements.BFF.Infrastructure;
using FinancialStatements.BFF.Models;
using FinancialStatements.Models.Enums;
using FinancialStatements.Models.Events;

namespace FinancialStatements.BFF.Delegates;

public sealed class AnnualStatementDelegate : IStatementDelegate
{
    private readonly ISqsPublisher _sqsPublisher;
    private readonly ILogger<AnnualStatementDelegate> _logger;

    public AnnualStatementDelegate(ISqsPublisher sqsPublisher, ILogger<AnnualStatementDelegate> logger)
    {
        _sqsPublisher = sqsPublisher;
        _logger = logger;
    }

    public bool CanHandle(StatementType type) => type == StatementType.Annual;

    public async Task<StatementResult> ExecuteAsync(StatementRequest request, CancellationToken ct = default)
    {
        var documentId = Guid.NewGuid().ToString("N");

        var @event = new StatementRequestedEvent
        {
            CorrelationId = Guid.NewGuid().ToString(),
            DocumentId = documentId,
            UserId = request.UserId,
            AccountId = request.AccountId,
            Type = StatementType.Annual,
            From = new DateTimeOffset(request.From.Year, 1, 1, 0, 0, 0, TimeSpan.Zero),
            To = new DateTimeOffset(request.To.Year, 12, 31, 23, 59, 59, TimeSpan.Zero),
            ResponseQueueUrl = _sqsPublisher.ResponseQueueUrl,
            RequestedAt = DateTimeOffset.UtcNow
        };

        await _sqsPublisher.PublishAsync(@event, ct);
        _logger.LogInformation("Annual statement requested. DocumentId={DocumentId}", documentId);

        return new StatementResult(Success: true, DocumentId: documentId, ErrorMessage: null);
    }
}
