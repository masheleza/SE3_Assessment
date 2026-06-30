using System.Text.Json;
using StackExchange.Redis;

namespace FinancialStatements.BFF.Infrastructure;

public interface ICacheService
{
    Task SetAsync<T>(string key, T value, TimeSpan expiry, CancellationToken ct = default);
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
}

public sealed class RedisCacheService : ICacheService
{
    private readonly IDatabase _db;

    public RedisCacheService(IConnectionMultiplexer redis) =>
        _db = redis.GetDatabase();

    public async Task SetAsync<T>(string key, T value, TimeSpan expiry, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(value);
        await _db.StringSetAsync(key, json, expiry);
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        var value = await _db.StringGetAsync(key);
        if (!value.HasValue) return default;
        return JsonSerializer.Deserialize<T>(value.ToString());
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default) =>
       await _db.KeyDeleteAsync(key);

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
        await _db.KeyExistsAsync(key);
}
