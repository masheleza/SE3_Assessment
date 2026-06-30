using FinancialStatements.BFF.Models;
using FinancialStatements.Models.Enums;

namespace FinancialStatements.BFF.Delegates;

public interface IStatementDelegate
{
    bool CanHandle(StatementType type);
    Task<StatementResult> ExecuteAsync(StatementRequest request, CancellationToken ct = default);
}
