using FinancialStatements.BFF.Infrastructure;
using FinancialStatements.BFF.Models;
using FinancialStatements.Models.DTOs.Response;

namespace FinancialStatements.BFF.Services;

public interface ISecureLinkService
{
    Task<SecureLinkResponseDto> GenerateAsync(string documentId, string userId, CancellationToken ct = default);
    Task<SecureLink?> ValidateAsync(string token, CancellationToken ct = default);
    Task MarkUsedAsync(string token, CancellationToken ct = default);
}

public sealed class SecureLinkService : ISecureLinkService
{
    private static readonly TimeSpan DefaultExpiry = TimeSpan.FromMinutes(30);
    private const string KeyPrefix = "secure-link:";

    private readonly ICacheService _cache;
    private readonly IConfiguration _config;

    public SecureLinkService(ICacheService cache, IConfiguration config)
    {
        _cache = cache;
        _config = config;
    }

    public async Task<SecureLinkResponseDto> GenerateAsync(string documentId, string userId, CancellationToken ct = default)
    {
        var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var expiry = TimeSpan.TryParse(_config["SecureLink:Expiry"], out var configured)
            ? configured
            : DefaultExpiry;

        var link = new SecureLink(
            Token: token,
            DocumentId: documentId,
            UserId: userId,
            ExpiresAt: DateTimeOffset.UtcNow.Add(expiry)
        );

        await _cache.SetAsync($"{KeyPrefix}{token}", link, expiry, ct);

        var baseUrl = _config["BFF:BaseUrl"] ?? "https://localhost:7001";

        return new SecureLinkResponseDto
        {
            LinkUrl = $"{baseUrl}/api/documents/download/{token}",
            ExpiresAt = link.ExpiresAt,
            DocumentId = documentId
        };
    }

    public Task<SecureLink?> ValidateAsync(string token, CancellationToken ct = default) =>
        _cache.GetAsync<SecureLink>($"{KeyPrefix}{token}", ct);

    public async Task MarkUsedAsync(string token, CancellationToken ct = default)
    {
        var link = await _cache.GetAsync<SecureLink>($"{KeyPrefix}{token}", ct);
        if (link is null) return;

        // Replace with IsUsed = true; retain remaining TTL via short window
        var remaining = link.ExpiresAt - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero) return;

        var used = link with { IsUsed = true };
        await _cache.SetAsync($"{KeyPrefix}{token}", used, remaining, ct);
    }
}
