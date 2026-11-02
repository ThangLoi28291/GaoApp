using Microsoft.Extensions.Caching.Memory;

namespace GaoApp.Web.Services.StoreMonitor;

public sealed record StoreMonitorSavedData(StoreDeliverySnapshot Delivery, IReadOnlyList<StoreCheckout> Checkouts);

/// <summary>Share one committed read per store/second across viewers, never cache authorization or DbContexts.</summary>
public sealed class StoreMonitorReadCache(TimeProvider time) : IDisposable
{
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 512 });
    private readonly SemaphoreSlim[] locks = Enumerable.Range(0, 32).Select(_ => new SemaphoreSlim(1)).ToArray();
    private sealed record Entry(DateTimeOffset At, StoreMonitorSavedData Data);
    public async Task<StoreMonitorSavedData> ReadAsync(int store, Func<Task<StoreMonitorSavedData>> read, CancellationToken ct)
    {
        var gate = locks[(uint)store % locks.Length];
        await gate.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue(store, out Entry? entry) && entry is not null && time.GetUtcNow() - entry.At < TimeSpan.FromSeconds(1))
                return entry.Data;
            var data = await read();
            cache.Set(store, new Entry(time.GetUtcNow(), data), new MemoryCacheEntryOptions { Size = 1, SlidingExpiration = TimeSpan.FromMinutes(5) });
            return data;
        }
        finally { gate.Release(); }
    }
    public void Dispose() { cache.Dispose(); foreach (var gate in locks) gate.Dispose(); }
}
