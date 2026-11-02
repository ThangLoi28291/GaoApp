using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.StoreMonitor;

public sealed record StoreCheckoutLine(string ItemName, string? UnitName, decimal Quantity, decimal UnitPrice, decimal LineTotal, int LineId = 0);
public sealed record StoreCheckout(int TerminalId, string Terminal, string Name, int? OrderId, string? Code,
    string? CustomerName, string? State, decimal GrandTotal, decimal PaidTotal, decimal BalanceDue, decimal ChangeDue,
    IReadOnlyList<StoreCheckoutLine> Lines);

/// <summary>Allowlisted, committed current carts. Never disclose contact details, notes, debt or images.</summary>
public sealed class StoreCheckoutMonitor(AppDbContext db)
{
    public async Task<IReadOnlyList<StoreCheckout>> ReadAsync(int store, CancellationToken ct)
    {
        if (store <= 0 || db.CurrentStoreId != store) return [];
        var terminals = await db.POSTerminals.AsNoTracking()
            .Where(x => x.StoreId == store && x.IsActive && !x.IsDeleted).OrderBy(x => x.Code).ThenBy(x => x.Id).Take(3)
            .Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(ct);
        var terminalIds = terminals.Select(x => x.Id).ToArray();
        var shifts = await db.POSShifts.AsNoTracking().Where(x => x.StoreId == store && !x.IsDeleted && x.Status == POSShiftStatus.Open && terminalIds.Contains(x.TerminalId))
            .Select(x => new { x.TerminalId, x.CurrentOrderId }).ToListAsync(ct);
        var ids = shifts.Where(x => x.CurrentOrderId.HasValue).Select(x => x.CurrentOrderId!.Value).Distinct().ToArray();
        var orders = await db.Orders.AsNoTracking().Where(x => x.StoreId == store && !x.IsDeleted && ids.Contains(x.Id))
            .Select(x => new { x.Id, x.OrderNumber, x.Status, CustomerName = x.Customer != null && x.Customer.StoreId == store && !x.Customer.IsDeleted ? x.Customer.Name : null,
                x.GrandTotal, x.PaidTotal, x.BalanceDue, x.ChangeDue }).ToDictionaryAsync(x => x.Id, ct);
        var lines = await db.OrderLines.AsNoTracking().Where(x => x.StoreId == store && !x.IsDeleted && ids.Contains(x.OrderId))
            .OrderBy(x => x.Id).Select(x => new { x.OrderId, Line = new StoreCheckoutLine(x.ItemName,
                x.SellingUnitName ?? x.UnitName, x.Quantity, x.UnitPrice, x.LineTotal, x.Id) }).ToListAsync(ct);
        return terminals.Select(terminal =>
        {
            var id = shifts.FirstOrDefault(x => x.TerminalId == terminal.Id)?.CurrentOrderId;
            var order = id.HasValue ? orders.GetValueOrDefault(id.Value) : null;
            return new StoreCheckout(terminal.Id, terminal.Code, terminal.Name, order?.Id, order?.OrderNumber,
                order?.CustomerName, order?.Status.ToString(), order?.GrandTotal ?? 0, order?.PaidTotal ?? 0,
                order?.BalanceDue ?? 0, order?.ChangeDue ?? 0,
                lines.Where(x => x.OrderId == order?.Id).Select(x => x.Line).ToArray());
        }).ToArray();
    }
}
