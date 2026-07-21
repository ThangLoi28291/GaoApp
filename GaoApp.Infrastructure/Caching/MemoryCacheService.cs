using GaoApp.Application.Common.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace GaoApp.Infrastructure.Caching;

/// <summary>
/// Implement cache service bằng IMemoryCache.
/// Đây là lớp Infrastructure, Application không phụ thuộc trực tiếp lớp này.
/// </summary>
public class MemoryCacheService : ICacheService
{
    private readonly IMemoryCache _cache;

    public MemoryCacheService(IMemoryCache cache)
    {
        _cache = cache;
    }

    public async Task<T?> GetOrCreateAsync<T>(
        string key,
        Func<Task<T>> factory,
        TimeSpan? expiry = null)
    {
        if (_cache.TryGetValue(key, out T? cachedValue))
        {
            return cachedValue;
        }

        var value = await factory();

        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiry ?? TimeSpan.FromMinutes(3)
        };

        _cache.Set(key, value, options);

        return value;
    }

    public void Remove(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        _cache.Remove(key);
    }
}