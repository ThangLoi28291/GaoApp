using System.Collections.Concurrent;

namespace GaoApp.Web.Services;

// Display intentions only. Monetary values and confirmation are always read from SQL.
public sealed class CustomerPaymentDisplayState
{
    public sealed record Intent(int ShiftId, int OrderId, string Mode, int? QrId,
        decimal Amount, int Method, long Version, DateTime AtUtc, DateTime ExpiresAtUtc);
    private readonly ConcurrentDictionary<(int Store, int Terminal), Intent> entries = new();
    private long version;

    public void Set(int store, int terminal, int shift, int order, string mode, int? qrId, decimal amount, int method)
    {
        var now = DateTime.UtcNow;
        entries[(store, terminal)] = new(shift, order, mode, qrId, amount, method,
            Interlocked.Increment(ref version), now, now.AddMinutes(15));
    }

    public Intent? Get(int store, int terminal)
    {
        if (!entries.TryGetValue((store, terminal), out var value)) return null;
        if (value.ExpiresAtUtc > DateTime.UtcNow) return value;
        ((ICollection<KeyValuePair<(int, int), Intent>>)entries).Remove(new((store, terminal), value));
        return null;
    }
}
