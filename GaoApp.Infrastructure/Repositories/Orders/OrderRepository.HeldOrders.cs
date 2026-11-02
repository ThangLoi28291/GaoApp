using GaoApp.Application.DTOs.POS;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Orders;

public sealed partial class OrderRepository
{
    public Task<List<HeldOrderDto>> GetHeldOrderSummariesByStoreAsync(int storeId, CancellationToken ct = default)
        => _db.Orders
            .AsNoTracking()
            .Where(o => o.StoreId == storeId && o.Status == OrderStatus.OnHold && !o.IsDeleted)
            .OrderByDescending(o => o.HeldAtUtc)
            .ThenByDescending(o => o.Id)
            .Select(o => new HeldOrderDto
            {
                OrderId = o.Id,
                HoldCode = o.HoldCode,
                HoldNote = o.HoldNote,
                HeldAtUtc = o.HeldAtUtc,
                LineCount = o.Lines.Count(l => !l.IsDeleted),
                TotalQuantity = o.Lines.Where(l => !l.IsDeleted).Sum(l => (decimal?)l.Quantity) ?? 0m,
                Subtotal = o.Lines.Where(l => !l.IsDeleted).Sum(l => (decimal?)l.LineTotal) ?? 0m,
                CustomerId = o.CustomerId,
                CustomerName = o.Customer != null ? o.Customer.Name : null,
                CustomerPhone = o.Customer != null ? o.Customer.Phone : null,
                PosShiftId = o.POSShiftId,
                ShiftCode = o.POSShift != null ? o.POSShift.ShiftCode : null,
                TerminalId = o.POSShift != null ? o.POSShift.TerminalId.ToString() : null,
                TerminalName = o.POSShift != null ? o.POSShift.Terminal.Name : null,
                HeldByUserId = o.POSShift != null ? (int?)o.POSShift.OpenedByUserId : null
            })
            .ToListAsync(ct);
}
