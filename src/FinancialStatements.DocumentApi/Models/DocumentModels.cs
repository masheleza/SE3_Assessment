using FinancialStatements.Models.Enums;

namespace FinancialStatements.DocumentApi.Models;

// EF Core entity — stays here because it is an infrastructure concern
// tied to DocumentDbContext. Shared contracts live in FinancialStatements.Models.

public class DocumentRecord
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public StatementType Type { get; set; }
    public DateTimeOffset PeriodFrom { get; set; }
    public DateTimeOffset PeriodTo { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Pending;
    public string? StoragePath { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}
