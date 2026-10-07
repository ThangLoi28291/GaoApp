using GaoApp.Application.DTOs.Reports;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Reports;

public sealed partial class OperationsReportService
{
    public async Task<DebtReportDto> DebtAsync(OperationsReportQueryDto query, CancellationToken ct)
    {
        var p = Period(query);
        return await Snapshot(async () => {
            var journal = await Bounded(db.Set<CustomerReceivableEntry>().AsNoTracking().Where(x => x.StoreId == StoreId && x.CreatedAtUtc < p.ToUtcExclusive)
                .Select(x => new { x.OrderId, x.CustomerId, x.Amount, x.CreatedAtUtc }), ct);
            var orders = await Bounded(db.Orders.IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId == StoreId && db.Set<CustomerReceivableEntry>().Any(y => y.StoreId == StoreId && y.OrderId == x.Id))
                .Select(x => new { x.Id, x.OrderNumber, x.CreditDueDate, x.CompletedAtUtc }), ct);
            var customers = await db.Customers.IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId == StoreId).Select(x => new { x.Id, x.Name }).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
            var suppliers = await db.Suppliers.IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId == StoreId).Select(x => new { x.Id, x.Name }).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
            var payables = await Bounded(db.Set<PurchasePayable>().AsNoTracking().Where(x => x.StoreId == StoreId && x.RecognizedAtUtc < p.ToUtcExclusive && x.Status != PurchasePayableStatus.Cancelled), ct);
            var documents = await db.StockDocuments.IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId == StoreId &&
                db.Set<PurchasePayable>().Any(y => y.StoreId == StoreId && y.StockDocumentId == x.Id && y.RecognizedAtUtc < p.ToUtcExclusive && y.Status != PurchasePayableStatus.Cancelled))
                .Select(x => new { x.Id, x.DocumentNo }).ToDictionaryAsync(x => x.Id, x => x.DocumentNo, ct);
            var paid = await Bounded(db.TreasuryEntries.AsNoTracking().Where(x => x.StoreId == StoreId && x.PurchasePayableId != null)
                .Select(x => new { x.PurchasePayableId, x.Amount, x.OccurredAtUtc }), ct);
            var orderMap = orders.ToDictionary(x => x.Id);
            var rows = new List<DebtReportRowDto>();
            var trendIncrease = new decimal[p.BucketCount]; var trendDecrease = new decimal[p.BucketCount];
            foreach (var group in journal.GroupBy(x => x.OrderId)) {
                var opening = group.Where(x => x.CreatedAtUtc < p.FromUtc).Sum(x => x.Amount);
                var current = group.Where(x => x.CreatedAtUtc >= p.FromUtc).ToList();
                var increase = current.Where(x => x.Amount > 0).Sum(x => x.Amount); var decrease = -current.Where(x => x.Amount < 0).Sum(x => x.Amount);
                var closing = opening + increase - decrease;
                if (opening == 0 && increase == 0 && decrease == 0) continue;
                foreach (var x in current) { if (x.Amount > 0) trendIncrease[Bucket(p, x.CreatedAtUtc)] += x.Amount; else trendDecrease[Bucket(p, x.CreatedAtUtc)] -= x.Amount; }
                var customerId = group.First().CustomerId; orderMap.TryGetValue(group.Key, out var order);
                rows.Add(new("receivable", group.Key, customerId, customers.GetValueOrDefault(customerId, $"Khách #{customerId}"), order?.OrderNumber ?? $"Đơn #{group.Key}",
                    order?.CompletedAtUtc ?? group.Min(x => x.CreatedAtUtc), order?.CreditDueDate, opening, increase, decrease, closing, Aging(order?.CreditDueDate, p.ToDate, closing)));
            }
            var payments = paid.ToLookup(x => x.PurchasePayableId!.Value);
            foreach (var x in payables) {
                var records = payments[x.Id].ToList();
                var settlements = records.Where(y => y.OccurredAtUtc < p.ToUtcExclusive).Select(y => (At: y.OccurredAtUtc, Amount: -y.Amount)).ToList();
                if (records.Count == 0 && x.Status == PurchasePayableStatus.Paid && x.PaidAtUtc.HasValue && x.PaidAtUtc < p.ToUtcExclusive)
                    settlements.Add((x.PaidAtUtc.Value, x.Amount));
                var opening = (x.RecognizedAtUtc < p.FromUtc ? x.Amount : 0) - settlements.Where(y => y.At < p.FromUtc).Sum(y => y.Amount);
                var increase = (x.RecognizedAtUtc >= p.FromUtc ? x.Amount : 0) - settlements.Where(y => y.At >= p.FromUtc && y.Amount < 0).Sum(y => y.Amount);
                var decrease = settlements.Where(y => y.At >= p.FromUtc && y.Amount > 0).Sum(y => y.Amount);
                var closing = opening + increase - decrease;
                if (opening == 0 && increase == 0 && decrease == 0) continue;
                rows.Add(new("payable", x.Id, x.SupplierId ?? 0, x.PayeeName ?? suppliers.GetValueOrDefault(x.SupplierId ?? 0, "Bên nhận khác"),
                    $"{documents.GetValueOrDefault(x.StockDocumentId, "Nhập kho #" + x.StockDocumentId)} · {(x.Type == PurchasePayableType.Freight ? "Vận chuyển" : "Tiền hàng")}", x.RecognizedAtUtc, x.DueDate, opening, increase, decrease, closing, Aging(x.DueDate, p.ToDate, closing), Version(x.RowVersion)));
            }
            var warnings = new List<string>();
            if (rows.Any(x => x.Closing > 0 && x.DueDate == null)) warnings.Add("Khoản chưa có hạn thanh toán được tách riêng, không tự coi là quá hạn. Có thể cập nhật hạn nợ nhà cung cấp.");
            if (payables.Any(x => x.Status == PurchasePayableStatus.Paid && x.PaidAtUtc == null)) warnings.Add("Có khoản NCC đã đánh dấu thanh toán nhưng thiếu ngày thanh toán; dư nợ lịch sử chưa đủ căn cứ và cần đối chiếu chứng từ.");
            warnings.Add("Dư nợ khách hàng tính từ nhật ký công nợ đến cuối ngày đã chọn. Giảm nợ gồm thu tiền, trả hàng và hủy đơn; không đồng nghĩa toàn bộ là dòng tiền thu.");
            warnings.Add("Hạn thanh toán dùng thông tin hiện tại của chứng từ. Khoản NCC đã hủy được loại theo trạng thái hiện tại.");
            return new DebtReportDto(p, rows.OrderByDescending(x => x.Closing).ThenBy(x => x.Party).ToList(),
                rows.Where(x => x.Kind == "receivable").Sum(x => x.Closing), rows.Where(x => x.Kind == "payable").Sum(x => x.Closing),
                rows.Where(x => x.Kind == "receivable" && IsOverdue(x.Aging)).Sum(x => x.Closing), rows.Where(x => x.Kind == "payable" && IsOverdue(x.Aging)).Sum(x => x.Closing),
                Enumerable.Range(0, p.BucketCount).Select(i => new DebtTrendDto(periods.FormatBucketLabel(p, i), trendIncrease[i], trendDecrease[i])).ToList(), warnings);
        }, ct);
    }
    private static bool IsOverdue(string aging) => aging is "1-30" or "31-60" or "61-90" or "90+";
    private static string Aging(DateTime? due, DateTime end, decimal closing) {
        if (closing <= 0) return "settled";
        if (due == null) return "unknown";
        var days = (end.Date - due.Value.Date).Days;
        return days <= 0 ? "current" : days <= 30 ? "1-30" : days <= 60 ? "31-60" : days <= 90 ? "61-90" : "90+";
    }
}
