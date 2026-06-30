namespace FinancialStatements.DocumentApi.Tests.Infrastructure;

public sealed class DocumentRepositoryTests : IDisposable
{
    private readonly DocumentDbContext _db;
    private readonly DocumentRepository _sut;

    public DocumentRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<DocumentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new DocumentDbContext(options);
        _sut = new DocumentRepository(_db);
    }

    [Fact]
    public async Task GetByIdAsync_WhenRecordExists_ReturnsRecord()
    {
        var record = BuildRecord();
        _db.Documents.Add(record);
        await _db.SaveChangesAsync();

        var result = await _sut.GetByIdAsync(record.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(record.Id);
        result.UserId.Should().Be(record.UserId);
    }

    [Fact]
    public async Task GetByIdAsync_WhenRecordDoesNotExist_ReturnsNull()
    {
        var result = await _sut.GetByIdAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpsertAsync_WhenNewRecord_Inserts()
    {
        var record = BuildRecord();

        await _sut.UpsertAsync(record);

        _db.Documents.Should().ContainSingle(d => d.Id == record.Id);
    }

    [Fact]
    public async Task UpsertAsync_WhenExistingRecord_UpdatesInPlace()
    {
        var record = BuildRecord(status: DocumentStatus.Pending);
        _db.Documents.Add(record);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var updated = new DocumentRecord
        {
            Id = record.Id,
            UserId = record.UserId,
            AccountId = record.AccountId,
            Type = record.Type,
            PeriodFrom = record.PeriodFrom,
            PeriodTo = record.PeriodTo,
            CreatedAt = record.CreatedAt,
            Status = DocumentStatus.Ready,
            StoragePath = "path/to/doc.pdf"
        };
        await _sut.UpsertAsync(updated);

        var stored = await _db.Documents.FindAsync(record.Id);
        stored!.Status.Should().Be(DocumentStatus.Ready);
        stored.StoragePath.Should().Be("path/to/doc.pdf");
        _db.Documents.Should().HaveCount(1);
    }

    [Fact]
    public async Task UpdateStatusAsync_SetsCorrectStatus()
    {
        var record = BuildRecord(status: DocumentStatus.Processing);
        _db.Documents.Add(record);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await _sut.UpdateStatusAsync(record.Id, DocumentStatus.Ready, "path/doc.pdf", null);

        var stored = await _db.Documents.FindAsync(record.Id);
        stored!.Status.Should().Be(DocumentStatus.Ready);
        stored.StoragePath.Should().Be("path/doc.pdf");
    }

    [Fact]
    public async Task UpdateStatusAsync_WhenStatusIsReady_SetsCompletedAt()
    {
        var record = BuildRecord(status: DocumentStatus.Processing);
        _db.Documents.Add(record);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var before = DateTimeOffset.UtcNow;
        await _sut.UpdateStatusAsync(record.Id, DocumentStatus.Ready, "path/doc.pdf", null);

        var stored = await _db.Documents.FindAsync(record.Id);
        stored!.CompletedAt.Should().NotBeNull()
            .And.Subject.As<DateTimeOffset>().Should().BeCloseTo(before, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task UpdateStatusAsync_WhenStatusIsFailed_SetsErrorMessage()
    {
        var record = BuildRecord(status: DocumentStatus.Processing);
        _db.Documents.Add(record);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await _sut.UpdateStatusAsync(record.Id, DocumentStatus.Failed, null, "disk full");

        var stored = await _db.Documents.FindAsync(record.Id);
        stored!.Status.Should().Be(DocumentStatus.Failed);
        stored.ErrorMessage.Should().Be("disk full");
    }

    [Fact]
    public async Task UpdateStatusAsync_WhenStatusIsNotReady_CompletedAtIsNull()
    {
        var record = BuildRecord(status: DocumentStatus.Processing);
        _db.Documents.Add(record);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await _sut.UpdateStatusAsync(record.Id, DocumentStatus.Failed, null, "error");

        var stored = await _db.Documents.FindAsync(record.Id);
        stored!.CompletedAt.Should().BeNull();
    }

    private static DocumentRecord BuildRecord(DocumentStatus status = DocumentStatus.Pending) => new()
    {
        Id = Guid.NewGuid(),
        UserId = "user-1",
        AccountId = "acc-1",
        Type = StatementType.Monthly,
        PeriodFrom = DateTimeOffset.UtcNow.AddMonths(-1),
        PeriodTo = DateTimeOffset.UtcNow,
        Status = status
    };

    public void Dispose() => _db.Dispose();
}
