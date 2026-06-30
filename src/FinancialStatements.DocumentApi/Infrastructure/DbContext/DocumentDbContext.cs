using FinancialStatements.DocumentApi.Models;
using Microsoft.EntityFrameworkCore;

namespace FinancialStatements.DocumentApi.Infrastructure.DbContext;

public sealed class DocumentDbContext : Microsoft.EntityFrameworkCore.DbContext
{
    public DocumentDbContext(DbContextOptions<DocumentDbContext> options) : base(options) { }

    public DbSet<DocumentRecord> Documents => Set<DocumentRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.UserId).HasMaxLength(256).IsRequired();
            entity.Property(e => e.AccountId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.StoragePath).HasMaxLength(1024);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2048);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.UserId, e.AccountId });
        });
    }
}
