namespace FinancialStatements.BFF.Tests.Services;

public sealed class SecureLinkServiceTests
{
    private readonly Mock<ICacheService> _cache = new();
    private readonly IConfiguration _config = BuildConfig();
    private readonly SecureLinkService _sut;

    public SecureLinkServiceTests() =>
        _sut = new SecureLinkService(_cache.Object, _config);

    [Fact]
    public async Task GenerateAsync_StoresLinkInCacheWithExpiry()
    {
        TimeSpan? capturedExpiry = null;
        _cache.Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<SecureLink>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, SecureLink, TimeSpan, CancellationToken>((_, _, expiry, _) =>
                capturedExpiry = expiry)
            .Returns(Task.CompletedTask);

        await _sut.GenerateAsync("doc-1", "user-1");

        capturedExpiry.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task GenerateAsync_ReturnsResponseWithCorrectDocumentId()
    {
        _cache.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<SecureLink>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _sut.GenerateAsync("doc-42", "user-1");

        result.DocumentId.Should().Be("doc-42");
    }

    [Fact]
    public async Task GenerateAsync_ReturnsLinkUrlContainingToken()
    {
        string? cacheKey = null;
        _cache.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<SecureLink>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback<string, SecureLink, TimeSpan, CancellationToken>((key, _, _, _) => cacheKey = key)
            .Returns(Task.CompletedTask);

        var result = await _sut.GenerateAsync("doc-1", "user-1");

        // Token is the suffix after "secure-link:" in the cache key
        var token = cacheKey!.Replace("secure-link:", "");
        result.LinkUrl.Should().Contain(token);
    }

    [Fact]
    public async Task GenerateAsync_ExpiresAtIsApproximately30MinutesFromNow()
    {
        _cache.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<SecureLink>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var before = DateTimeOffset.UtcNow;
        var result = await _sut.GenerateAsync("doc-1", "user-1");

        result.ExpiresAt.Should().BeCloseTo(before.AddMinutes(30), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ValidateAsync_WhenTokenExists_ReturnsLink()
    {
        var existing = new SecureLink("tok", "doc-1", "user-1", DateTimeOffset.UtcNow.AddMinutes(30));
        _cache.Setup(c => c.GetAsync<SecureLink>("secure-link:tok", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await _sut.ValidateAsync("tok");

        result.Should().BeEquivalentTo(existing);
    }

    [Fact]
    public async Task ValidateAsync_WhenTokenNotFound_ReturnsNull()
    {
        _cache.Setup(c => c.GetAsync<SecureLink>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SecureLink?)null);

        var result = await _sut.ValidateAsync("unknown");

        result.Should().BeNull();
    }

    [Fact]
    public async Task MarkUsedAsync_WhenTokenExists_SavesIsUsedTrue()
    {
        var existing = new SecureLink("tok", "doc-1", "user-1", DateTimeOffset.UtcNow.AddMinutes(25));
        _cache.Setup(c => c.GetAsync<SecureLink>("secure-link:tok", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        SecureLink? saved = null;
        _cache.Setup(c => c.SetAsync(
                "secure-link:tok",
                It.IsAny<SecureLink>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, SecureLink, TimeSpan, CancellationToken>((_, link, _, _) => saved = link)
            .Returns(Task.CompletedTask);

        await _sut.MarkUsedAsync("tok");

        saved!.IsUsed.Should().BeTrue();
    }

    [Fact]
    public async Task MarkUsedAsync_WhenTokenNotFound_DoesNotCallSetAsync()
    {
        _cache.Setup(c => c.GetAsync<SecureLink>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SecureLink?)null);

        await _sut.MarkUsedAsync("missing");

        _cache.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<SecureLink>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MarkUsedAsync_WhenTokenAlreadyExpired_DoesNotCallSetAsync()
    {
        var expired = new SecureLink("tok", "doc-1", "user-1", DateTimeOffset.UtcNow.AddMinutes(-1));
        _cache.Setup(c => c.GetAsync<SecureLink>("secure-link:tok", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expired);

        await _sut.MarkUsedAsync("tok");

        _cache.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<SecureLink>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static IConfiguration BuildConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecureLink:Expiry"] = "00:30:00",
                ["BFF:BaseUrl"] = "https://localhost:7001"
            })
            .Build();
}
