using FinancialStatements.Models.Enums;

namespace FinancialStatements.BFF.Models;

// Internal domain objects — not exposed over HTTP or SQS.
// Shared contracts live in FinancialStatements.Models.

public record StatementRequest(
    string UserId,
    string AccountId,
    StatementType Type,
    DateTimeOffset From,
    DateTimeOffset To,
    string ConnectionId
);

public record StatementResult(
    bool Success,
    string? DocumentId,
    string? ErrorMessage
);

public record SecureLink(
    string Token,
    string DocumentId,
    string UserId,
    DateTimeOffset ExpiresAt,
    bool IsUsed = false
);
