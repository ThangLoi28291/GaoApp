using System.Diagnostics.Metrics;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Security;

public readonly record struct ResourceSubject
{
    internal string? Key { get; }
    private ResourceSubject(string key) => Key = key;
    public static ResourceSubject User(int id) => new("user:" + Positive(id));
    public static ResourceSubject Visitor(Guid id) => id != Guid.Empty ? new("visitor:" + id.ToString("N")) : throw new ArgumentOutOfRangeException(nameof(id));
    public static ResourceSubject Device(int id) => new("device:" + Positive(id));
    public static ResourceSubject Ip(IPAddress? address) => new("ip:" + (address?.MapToIPv6().ToString() ?? "unknown"));
    private static int Positive(int id) => id > 0 ? id : throw new ArgumentOutOfRangeException(nameof(id));
}

public sealed class ResourceLease : IDisposable
{
    private Action? release;
    public bool IsAcquired { get; }
    public string? Reason { get; }
    public int RetryAfterSeconds { get; }
    internal ResourceLease(bool acquired, string? reason = null, int retry = 1, Action? release = null)
    { IsAcquired = acquired; Reason = reason; RetryAfterSeconds = retry; this.release = release; }
    public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
}

/// <summary>Bounded keys, no waiting queue, atomic multi-scope admission, and finally-safe concurrency leases.</summary>
public sealed class ResourceAdmission : IDisposable
{
    private readonly object sync = new();
    private readonly ResourceProtectionOptions options;
    private readonly ILogger<ResourceAdmission> logger;
    private readonly Dictionary<ResourceGroup, Group> groups = new();
    private bool disposed;
    private static readonly Meter Meter = new("GaoApp.ResourceProtection", "1.0");
    private static readonly Counter<long> Rejections = Meter.CreateCounter<long>("gaoapp.resource.rejections");

    private sealed class Entry(int permits, int concurrency, int window)
    {
        public readonly WindowBudget? Rate = permits == 0 ? null : new(permits, window);
        public readonly ConcurrencyLimiter? Concurrent = concurrency == 0 ? null : new(new() { PermitLimit = concurrency, QueueLimit = 0 });
        public int Active;
        public long LastUsed = Environment.TickCount64;
        public void Dispose() { Concurrent?.Dispose(); }
    }
    // Six bounded counters per key. Advance all elapsed segments on access, including after a long idle.
    // No per-partition timers, background work, unbounded timestamp lists or waiting requests.
    private sealed class WindowBudget(int limit, int seconds)
    {
        private readonly int[] buckets = new int[6];
        private readonly long windowMs = seconds * 1000L;
        private long segment = Environment.TickCount64 * 6 / (seconds * 1000L);
        private int used;
        public int Available => limit - used;
        public void Replenish(long now)
        {
            var current = now * 6 / windowMs;
            for (var i = 1L; i <= Math.Min(6, current - segment); i++)
            {
                var index = (int)((segment + i) % 6);
                used -= buckets[index]; buckets[index] = 0;
            }
            segment = current;
        }
        public void Consume() { buckets[(int)(segment % 6)]++; used++; }
        public int RetrySeconds(long now)
        {
            for (var i = 1; i <= 6; i++)
                if (buckets[(int)((segment + i) % 6)] > 0)
                    return Math.Max(1, (int)Math.Ceiling(((segment + i) * windowMs / 6d - now) / 1000d));
            return 1;
        }
    }
    private sealed class Group
    {
        public readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);
        public long LastSweep;
        public readonly Dictionary<string, long> LastWarnings = new(StringComparer.Ordinal);
    }

    public ResourceAdmission(IOptions<ResourceProtectionOptions> options, ILogger<ResourceAdmission> logger)
    {
        this.options = options.Value; this.logger = logger;
        if (!this.options.IsValid()) throw new OptionsValidationException(ResourceProtectionOptions.Section,
            typeof(ResourceProtectionOptions), ["Invalid resource protection limits."]);
    }

    public ResourceLease Enter(ResourceGroup name, ResourceSubject subject = default, int? storeId = null)
    {
        if (!Enum.IsDefined(name)) throw new ArgumentOutOfRangeException(nameof(name));
        if (storeId is <= 0) throw new ArgumentOutOfRangeException(nameof(storeId));
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var p = options.Limits[name];
            if (!groups.TryGetValue(name, out var group)) groups.Add(name, group = new());
            var rules = new List<(string Key, int Rate, int Concurrent)> { ("process", p.ProcessPerWindow, p.ProcessConcurrency) };
            if (storeId.HasValue && (p.StorePerWindow > 0 || p.StoreConcurrency > 0)) rules.Add(("store:" + storeId.Value, p.StorePerWindow, p.StoreConcurrency));
            if (subject.Key is not null && (p.CallerPerWindow > 0 || p.CallerConcurrency > 0)) rules.Add((subject.Key, p.CallerPerWindow, p.CallerConcurrency));
            var missing = rules.Count(x => !group.Entries.ContainsKey(x.Key));
            var now = Environment.TickCount64;
            if (group.Entries.Count + missing > options.MaxPartitionsPerGroup && now - group.LastSweep >= 1000)
            {
                group.LastSweep = now;
                foreach (var key in group.Entries.Where(x => x.Value.Active == 0 && now - x.Value.LastUsed >= options.IdleSeconds * 1000L).Select(x => x.Key).ToArray())
                { group.Entries[key].Dispose(); group.Entries.Remove(key); }
            }
            if (group.Entries.Count + missing > options.MaxPartitionsPerGroup) return Reject(name, group, p, "capacity", 1);
            var entries = new List<Entry>();
            foreach (var rule in rules)
            {
                if (!group.Entries.TryGetValue(rule.Key, out var entry))
                    group.Entries.Add(rule.Key, entry = new(rule.Rate, rule.Concurrent, options.WindowSeconds));
                entry.LastUsed = now;
                entry.Rate?.Replenish(now);
                entries.Add(entry);
            }
            // All requests and releases use this lock. Check every scope before consuming a time permit.
            foreach (var entry in entries)
            {
                if (entry.Concurrent?.GetStatistics()?.CurrentAvailablePermits == 0) return Reject(name, group, p, "concurrency", 1);
                if (entry.Rate?.Available == 0)
                {
                    var retry = entry.Rate.RetrySeconds(now);
                    return Reject(name, group, p, "frequency", Math.Clamp(retry, 1, options.WindowSeconds));
                }
            }
            var leases = new List<RateLimitLease>();
            foreach (var entry in entries)
            {
                entry.Rate?.Consume();
                if (entry.Concurrent is not null) leases.Add(entry.Concurrent.AttemptAcquire());
                entry.Active++;
            }
            return new(true, release: () =>
            {
                lock (sync)
                {
                    foreach (var lease in leases) lease.Dispose();
                    foreach (var entry in entries) { entry.Active--; entry.LastUsed = Environment.TickCount64; }
                }
            });
        }
    }

    private ResourceLease Reject(ResourceGroup name, Group group, ResourceLimit p, string reason, int retry)
    {
        // Accepted bank evidence must reach the durable inbox, including when an older config enabled enforcement.
        var observeOnly = p.ObserveOnly || name == ResourceGroup.BankAccepted;
        Rejections.Add(1, new KeyValuePair<string, object?>("group", name.ToString()),
            new("reason", reason), new("mode", observeOnly ? "observe" : "enforce"));
        var now = Environment.TickCount64;
        if (!group.LastWarnings.TryGetValue(reason, out var last) || now - last >= 10000)
        {
            group.LastWarnings[reason] = now;
            logger.LogWarning("Resource admission limit. Group={ResourceGroup}; Reason={Reason}; ObserveOnly={ObserveOnly}", name, reason, observeOnly);
        }
        return new(observeOnly, reason, retry);
    }

    public int PartitionCount(ResourceGroup group) { lock (sync) return groups.TryGetValue(group, out var value) ? value.Entries.Count : 0; }
    public void Dispose()
    {
        lock (sync) { if (disposed) return; disposed = true; foreach (var entry in groups.Values.SelectMany(x => x.Entries.Values)) entry.Dispose(); groups.Clear(); }
    }
}
