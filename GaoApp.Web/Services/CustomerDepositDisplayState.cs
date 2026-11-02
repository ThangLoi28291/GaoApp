using System.Collections.Concurrent;
using GaoApp.Application.Interfaces.Services.Orders;

namespace GaoApp.Web.Services;

public sealed class CustomerDepositDisplayState
{
    public sealed record ActiveQr(DepositQrPreviewDto Qr, DateTime ExpiresAtUtc, int ShiftId, int? OrderId);
    public sealed record Receipt(int ShiftId, int? OrderId, int EntryId, decimal Amount, DateTime ExpiresAtUtc);
    private readonly ConcurrentDictionary<(int StoreId, int TerminalId), ActiveQr> active = new();
    private readonly ConcurrentDictionary<(int StoreId, int TerminalId), Receipt> receipts = new();

    public void Set(int storeId, int terminalId, DepositQrPreviewDto qr, int shiftId = 0, int? orderId = null)
    {
        receipts.TryRemove((storeId, terminalId), out _);
        active[(storeId, terminalId)] = new(qr, DateTime.UtcNow.AddMinutes(15), shiftId, orderId);
    }

    public DepositQrPreviewDto? Get(int storeId, int terminalId)
        => GetState(storeId, terminalId)?.Qr;

    public ActiveQr? GetState(int storeId, int terminalId)
    {
        if (!active.TryGetValue((storeId, terminalId), out var value)) return null;
        if (value.ExpiresAtUtc > DateTime.UtcNow) return value;
        ((ICollection<KeyValuePair<(int, int), ActiveQr>>)active).Remove(new((storeId, terminalId), value));
        return null;
    }

    public void Clear(int storeId, int terminalId)
    {
        active.TryRemove((storeId, terminalId), out _);
        receipts.TryRemove((storeId, terminalId), out _);
    }

    public void SetReceipt(int store, int terminal, int shift, int? order, int entry, decimal amount, DateTime expires)
        => receipts[(store, terminal)] = new(shift, order, entry, amount, expires);

    public Receipt? GetReceipt(int store, int terminal)
    {
        if (!receipts.TryGetValue((store, terminal), out var value)) return null;
        if (value.ExpiresAtUtc > DateTime.UtcNow) return value;
        ((ICollection<KeyValuePair<(int, int), Receipt>>)receipts).Remove(new((store, terminal), value));
        return null;
    }
}
