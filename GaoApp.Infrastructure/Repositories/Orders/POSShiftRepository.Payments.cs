using GaoApp.Application.DTOs.POSShiftDashboards;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Data.Repositories.Orders;

public partial class POSShiftRepository
{
    public async Task<Dictionary<int, POSShiftPaymentBreakdownDto>> GetPaymentBreakdownsAsync(int storeId,
        IReadOnlyCollection<int> shiftIds, CancellationToken ct = default)
    {
        if (storeId <= 0 || shiftIds.Count == 0) return [];
        var ids = shiftIds.Distinct().ToArray();
        // One narrow query for all filtered shifts, without lines, payment metadata or QR images.
        // Debt collection/deposits are shown separately in reconciliation, never counted as sale revenue here.
        var payments = await _db.OrderPayments.AsNoTracking()
            .Where(p => p.StoreId == storeId && !p.IsDeleted && !p.IsDebtCollection && p.Amount > 0
                && p.Method != PaymentMethod.Cash && p.Order.StoreId == storeId && !p.Order.IsDeleted
                && ids.Contains(p.Order.POSShiftId)
                && !_db.Set<GaoApp.Domain.Storefront.StorefrontSaleBinding>().Any(b => b.StoreId == storeId && b.SaleOrderId == p.OrderId)
                && (p.Order.Status == OrderStatus.Completed || p.Order.Status == OrderStatus.Refunded))
            .Select(p => new
            {
                p.Id, p.OrderId, p.Method, p.Amount, ShiftId = p.Order.POSShiftId,
                p.Order.GrandTotal, p.Order.DepositAmount,
                Verified = p.Method == PaymentMethod.BankTransfer && _db.Set<AcbQrSession>().IgnoreQueryFilters().AsNoTracking().Any(s =>
                    s.StoreId == storeId && s.PaymentId == p.Id && s.ShiftId == p.Order.POSShiftId && s.OrderId == p.OrderId
                    && s.ConfirmationSource.HasValue && s.ConfirmationSource != AcbConfirmationSource.OfflineManual
                    && (s.Status == AcbSessionStatus.Received || s.Status == AcbSessionStatus.Completed)
                    && s.Amount == p.Amount && s.ProviderOrderId == p.ReferenceCode),
                AutomatedEvidence = p.Method == PaymentMethod.BankTransfer && (
                    _db.Set<AcbQrSession>().IgnoreQueryFilters().AsNoTracking().Any(s => s.StoreId == storeId && s.PaymentId == p.Id)
                    || _db.PosPaymentQrRequests.IgnoreQueryFilters().AsNoTracking().Any(q => q.StoreId == storeId && q.PaymentId == p.Id && q.ConfirmMode != BankQrConfirmMode.Manual)),
                OfflineManual = p.Method == PaymentMethod.BankTransfer && _db.Set<AcbQrSession>().IgnoreQueryFilters().AsNoTracking().Any(s =>
                    s.StoreId == storeId && s.PaymentId == p.Id && s.ShiftId == p.Order.POSShiftId && s.OrderId == p.OrderId
                    && s.ConfirmationSource == AcbConfirmationSource.OfflineManual && s.Amount == p.Amount && s.ProviderOrderId == p.ReferenceCode)
            }).ToListAsync(ct);
        var result = new Dictionary<int, POSShiftPaymentBreakdownDto>();
        foreach (var order in payments.GroupBy(x => x.OrderId))
        {
            var first = order.First();
            if (!result.TryGetValue(first.ShiftId, out var totals)) result[first.ShiftId] = totals = new();
            var remaining = Math.Max(0, first.GrandTotal - first.DepositAmount);
            foreach (var payment in order.OrderBy(x => x.Id))
            {
                var channel = POSShiftPaymentChannels.Resolve(payment.Method, payment.Verified, payment.AutomatedEvidence, payment.OfflineManual);
                var row = totals.Channels.Single(x => x.Channel == channel);
                var applied = Math.Min(payment.Amount, remaining);
                row.PaymentCount++;
                row.RecordedAmount += payment.Amount;
                row.SalesAmount += applied;
                remaining -= applied;
            }
        }
        return result;
    }
}
