using System.Collections.Concurrent;
using GaoApp.Application.Interfaces.Services.Orders;

namespace GaoApp.Web.Services;

public sealed class CustomerDepositDisplayState
{
    private sealed record ActiveQr(DepositQrPreviewDto Qr, DateTime ExpiresAtUtc);
    private readonly ConcurrentDictionary<(int StoreId, int TerminalId), ActiveQr> active = new();

    public void Set(int storeId, int terminalId, DepositQrPreviewDto qr)
        => active[(storeId, terminalId)] = new(qr, DateTime.UtcNow.AddMinutes(15));

    public DepositQrPreviewDto? Get(int storeId, int terminalId)
    {
        if (!active.TryGetValue((storeId, terminalId), out var value)) return null;
        if (value.ExpiresAtUtc > DateTime.UtcNow) return value.Qr;
        active.TryRemove((storeId, terminalId), out _);
        return null;
    }

    public void Clear(int storeId, int terminalId)
        => active.TryRemove((storeId, terminalId), out _);
}
