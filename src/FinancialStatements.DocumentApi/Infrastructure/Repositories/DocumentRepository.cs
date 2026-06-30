using FinancialStatements.DocumentApi.Infrastructure.DbContext;
using FinancialStatements.DocumentApi.Models;
using FinancialStatements.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinancialStatements.DocumentApi.Infrastructure.Repositories;

public interface IDocumentRepository
{
    Task<DocumentRecord?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task UpsertAsync(DocumentRecord record, CancellationToken ct = default);
    Task UpdateStatusAsync(Guid id, DocumentStatus status, string? storagePath, string? error, CancellationToken ct = default);
}

public sealed class DocumentRepository : IDocumentRepository
{
    private readonly DocumentDbContext _db;

    public DocumentRepository(DocumentDbContext db) => _db = db;

    public Task<DocumentRecord?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task UpsertAsync(DocumentRecord record, CancellationToken ct = default)
    {
        var existing = await _db.Documents.FindAsync([record.Id], ct);
        if (existing is null)
            _db.Documents.Add(record);
        else
            _db.Entry(existing).CurrentValues.SetValues(record);

        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateStatusAsync(
        Guid id, DocumentStatus status, string? storagePath, string? error, CancellationToken ct = default)
    {
        await _db.Documents
            .Where(d => d.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, status)
                .SetProperty(d => d.StoragePath, storagePath)
                .SetProperty(d => d.ErrorMessage, error)
                .SetProperty(d => d.CompletedAt, status == DocumentStatus.Ready ? DateTimeOffset.UtcNow : (DateTimeOffset?)null),
            ct);
    }
}
