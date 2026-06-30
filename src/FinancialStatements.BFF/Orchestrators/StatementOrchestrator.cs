using FinancialStatements.BFF.Delegates;
using FinancialStatements.BFF.Models;
using FinancialStatements.BFF.Services;
using FinancialStatements.Models.DTOs.Response;

namespace FinancialStatements.BFF.Orchestrators;

public interface IStatementOrchestrator
{
    Task<SecureLinkResponseDto> RequestStatementAsync(StatementRequest request, CancellationToken ct = default);
}

public sealed class StatementOrchestrator : IStatementOrchestrator
{
    private readonly IEnumerable<IStatementDelegate> _delegates;
    private readonly ISecureLinkService _secureLinkService;
    private readonly ILogger<StatementOrchestrator> _logger;

    public StatementOrchestrator(
        IEnumerable<IStatementDelegate> delegates,
        ISecureLinkService secureLinkService,
        ILogger<StatementOrchestrator> logger)
    {
        _delegates = delegates;
        _secureLinkService = secureLinkService;
        _logger = logger;
    }

    public async Task<SecureLinkResponseDto> RequestStatementAsync(StatementRequest request, CancellationToken ct = default)
    {
        var handler = _delegates.FirstOrDefault(d => d.CanHandle(request.Type))
            ?? throw new InvalidOperationException($"No delegate registered for statement type '{request.Type}'.");

        _logger.LogInformation(
            "Orchestrator dispatching {Type} statement for User={UserId} to {Delegate}",
            request.Type, request.UserId, handler.GetType().Name);

        var result = await handler.ExecuteAsync(request, ct);

        if (!result.Success || result.DocumentId is null)
            throw new InvalidOperationException(result.ErrorMessage ?? "Statement processing failed.");

        var link = await _secureLinkService.GenerateAsync(result.DocumentId, request.UserId, ct);

        _logger.LogInformation(
            "Secure link generated for DocumentId={DocumentId}, expires {ExpiresAt}",
            result.DocumentId, link.ExpiresAt);

        return link;
    }
}
