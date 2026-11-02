using System.Data;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Reports;
using GaoApp.Application.DTOs.Reports.Sales;
using GaoApp.Application.Interfaces.Services.Reports;
using GaoApp.Application.Services.Reports;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Inventory;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Reports;

public sealed partial class OperationsReportService(AppDbContext db, SalesReportingPeriodPolicy periods) : IOperationsReportService
{
    private const int Limit = 100_000;
    private int StoreId => db.CurrentStoreId is > 0 ? db.CurrentStoreId.Value : throw new ForbiddenAppException("Chưa chọn cửa hàng.");
    private SalesReportPeriodDto Period(OperationsReportQueryDto query) {
        if (query.WarehouseId is <= 0 || query.LowStockThreshold < 0 || query.LowStockThreshold > 1_000_000 || query.Fund is not (null or "" or "all" or "cash" or "bank" or "other"))
            throw new ArgumentException("Bộ lọc báo cáo không hợp lệ.");
        var result = periods.Resolve(new SalesExecutiveDashboardQueryDto { FromDate = query.FromDate, ToDate = query.ToDate, Compare = "none" }, DateTime.UtcNow).Current.Period;
        if (result.FromDate.Year < 2000 || result.ToDate > DateTime.UtcNow.AddHours(7).Date) throw new ArgumentException("Chọn kỳ báo cáo đã phát sinh, từ năm 2000 đến hôm nay.");
        return result;
    }
    private async Task<T> Snapshot<T>(Func<Task<T>> read, CancellationToken ct) => await db.Database.CreateExecutionStrategy().ExecuteAsync(async () => {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Snapshot, ct);
        var result = await read(); await tx.CommitAsync(ct); return result;
    });
    private static async Task<List<T>> Bounded<T>(IQueryable<T> query, CancellationToken ct) {
        var rows = await query.Take(Limit + 1).ToListAsync(ct);
        if (rows.Count > Limit) throw new ArgumentException("Kỳ báo cáo có quá nhiều dữ liệu. Chọn kỳ ngắn hơn hoặc một kho cụ thể.");
        return rows;
    }
    private static string Fund(PaymentMethod method) => method == PaymentMethod.Cash ? "cash" : method == PaymentMethod.BankTransfer ? "bank" : "other";
    private static string FundName(string fund) => fund switch { "cash" => "Tiền mặt", "bank" => "Chuyển khoản", _ => "Thẻ, ví & khác" };
    private static string Version(byte[] bytes) => Convert.ToBase64String(bytes);
    private int Bucket(SalesReportPeriodDto p, DateTime at) => Math.Clamp((int)Math.Floor((at - p.FromUtc).TotalHours / p.BucketHours), 0, p.BucketCount - 1);

    public async Task<CashFlowReportDto> CashFlowAsync(OperationsReportQueryDto query, CancellationToken ct)
    {
        var p = Period(query);
        return await Snapshot(async () => {
            var warnings = new List<string>();
            var openings = await db.TreasuryOpeningBalances.AsNoTracking().Where(x => x.StoreId == StoreId).ToListAsync(ct);
            var start = openings.Select(x => x.AsOfDate.AddHours(-7)).Append(p.FromUtc).Min();
            var flows = new List<MoneyMovementDto>();
            var sales = await Bounded(db.Orders.AsNoTracking().Where(x => x.StoreId == StoreId && (x.Status == OrderStatus.Completed || x.Status == OrderStatus.Refunded)
                && x.CompletedAtUtc < p.ToUtcExclusive && (x.CompletedAtUtc >= start || x.Payments.Any(y => !y.IsDeleted && !y.IsDebtCollection && y.PaidAtUtc >= start)))
                .Select(x => new { x.Id, x.OrderNumber, x.CompletedAtUtc, x.GrandTotal, x.DepositAmount }), ct);
            // Join rather than a large IN list: all normal payments are needed to remove cash change accurately.
            var payments = await Bounded((from payment in db.OrderPayments.AsNoTracking()
                join order in db.Orders.AsNoTracking() on payment.OrderId equals order.Id
                where payment.StoreId == StoreId && order.StoreId == StoreId && !payment.IsDebtCollection
                    && (order.Status == OrderStatus.Completed || order.Status == OrderStatus.Refunded) && order.CompletedAtUtc < p.ToUtcExclusive
                    && (order.CompletedAtUtc >= start || order.Payments.Any(y => !y.IsDeleted && !y.IsDebtCollection && y.PaidAtUtc >= start))
                select new { payment.Id, payment.OrderId, payment.Method, payment.Amount, payment.PaidAtUtc, payment.ReferenceCode,payment.BankVerifiedReceiptId, payment.StorefrontCodReceiptId, payment.StorefrontCarrierWithholdingId }), ct);
            var groups = payments.ToLookup(x => x.OrderId);
            foreach (var order in sales) {
                var remaining = Math.Max(0, order.GrandTotal - order.DepositAmount);
                foreach (var payment in groups[order.Id].OrderBy(x => x.Method == PaymentMethod.Cash).ThenBy(x => x.PaidAtUtc).ThenBy(x => x.Id)) {
                    var amount = Math.Min(remaining, Math.Max(0, payment.Amount)); remaining -= amount;
                    var at = payment.PaidAtUtc > order.CompletedAtUtc ? payment.PaidAtUtc : order.CompletedAtUtc!.Value;
                    if (payment.BankVerifiedReceiptId==null&&payment.StorefrontCodReceiptId==null&&payment.StorefrontCarrierWithholdingId==null&&amount != 0 && at >= start && at < p.ToUtcExclusive) flows.Add(new($"SALE-{payment.Id}", at, Fund(payment.Method), order.OrderNumber ?? $"Đơn #{order.Id}", "Thu bán hàng", amount, payment.ReferenceCode));
                }
            }
            var paidBankStatuses=new[]{"COMPLETED","SUCCESS","PAID","APPROVED"};
            var bankReceipts=await Bounded(db.Set<BankVerifiedReceipt>().AsNoTracking().Where(x=>x.OriginStoreId==StoreId&&x.OccurredAtUtc>=start&&x.OccurredAtUtc<p.ToUtcExclusive&&paidBankStatuses.Contains(x.Status)&&x.AssetConfidence==BankReceiptAssetConfidence.CertifiedDistinct),ct);
            flows.AddRange(bankReceipts.Select(x=>new MoneyMovementDto($"BANK-{x.Id}",DateTime.SpecifyKind(x.OccurredAtUtc!.Value,DateTimeKind.Utc),"bank",x.ProviderReference,
                x.NeedsReview?"Ngân hàng thực nhận — cần đối soát":"Ngân hàng thực nhận",x.Amount,x.ExternalTransactionId,TimePrecision:x.TimePrecision,BankBusinessDate:x.BankBusinessDate)));
            // Unknown bank time cannot be bounded by observation time: a delayed observation may affect an older report.
            var undated=await Bounded(db.Set<BankVerifiedReceipt>().AsNoTracking().Where(x=>x.OriginStoreId==StoreId&&x.OccurredAtUtc==null&&paidBankStatuses.Contains(x.Status)),ct);
            var unattributed=await Bounded(db.Set<GaoApp.Domain.Storefront.StorefrontUnattributedBankReceipt>().AsNoTracking().Where(x=>x.StoreId==StoreId&&(x.OccurredAtUtc==null||x.OccurredAtUtc>=p.FromUtc&&x.OccurredAtUtc<p.ToUtcExclusive)),ct);
            var bankBalanceIncomplete=undated.Count>0||await db.Set<GaoApp.Domain.Storefront.StorefrontUnattributedBankReceipt>().AnyAsync(x=>x.StoreId==StoreId,ct)||
                await db.Set<BankVerifiedReceipt>().AnyAsync(x=>x.OriginStoreId==StoreId&&(x.AssetConfidence!=BankReceiptAssetConfidence.CertifiedDistinct||x.NeedsReview&&x.ReviewCode!="RECEIPT_AMOUNT_MISMATCH"&&x.ReviewCode!="LATE_RECEIPT_AFTER_CANCEL"&&x.ReviewCode!="UNEXPECTED_ADDITIONAL_RECEIPT"&&x.ReviewCode!="UNMATCHED_CHANNEL_BINDING"),ct);
            var legacyAmbiguity=await Bounded(db.Set<BankVerifiedReceipt>().AsNoTracking().Where(x=>x.OriginStoreId==StoreId&&x.AssetConfidence==BankReceiptAssetConfidence.LegacySourceUnproven&&(x.OccurredAtUtc==null||x.OccurredAtUtc>=p.FromUtc&&x.OccurredAtUtc<p.ToUtcExclusive)),ct);
            var identityAmbiguity=await Bounded(db.Set<BankVerifiedReceipt>().AsNoTracking().Where(x=>x.OriginStoreId==StoreId&&x.AssetConfidence==BankReceiptAssetConfidence.TransactionIdentityAmbiguous&&(x.OccurredAtUtc==null||x.OccurredAtUtc>=p.FromUtc&&x.OccurredAtUtc<p.ToUtcExclusive)),ct);
            var unproven=await Bounded(db.Set<BankVerifiedReceipt>().AsNoTracking().Where(x=>x.OriginStoreId==StoreId&&x.AssetConfidence==BankReceiptAssetConfidence.Unproven&&(x.OccurredAtUtc==null||x.OccurredAtUtc>=p.FromUtc&&x.OccurredAtUtc<p.ToUtcExclusive)),ct);
            var conflictingEvidence=await Bounded(db.Set<BankReceiptConflict>().AsNoTracking().Where(x=>x.AttemptedStoreId==StoreId),ct);
            var bankReviews=undated.Where(x=>x.AssetConfidence==BankReceiptAssetConfidence.CertifiedDistinct).Select(x=>new BankEvidenceReviewDto($"BANK-{x.Id}",x.Amount,$"Chưa xác minh ngày ngân hàng; chứng cứ tiếp nhận ngày {x.VerifiedAtUtc.AddHours(7):dd/MM/yyyy}, không coi là ngày thực nhận",null))
                .Concat(unattributed.Select(x=>new BankEvidenceReviewDto($"UNATTRIBUTED-{x.Id}",x.Amount,"Tài khoản nhận mâu thuẫn; chưa xác định quỹ/chủ thể nhận",x.BankBusinessDate?.ToString("dd/MM/yyyy"))))
                .Concat(legacyAmbiguity.Select(x=>new BankEvidenceReviewDto($"LEGACY-{x.Id}",x.Amount,"Trùng định danh chứng cứ cũ chưa có tài khoản bất biến; chưa cộng lần nữa",x.BankBusinessDate?.ToString("dd/MM/yyyy"))))
                .Concat(identityAmbiguity.Select(x=>new BankEvidenceReviewDto($"IDENTITY-{x.Id}",x.Amount,"Chưa chứng minh định danh giữa hai giao thức là cùng hay khác giao dịch; không cộng thành lần thu mới",x.BankBusinessDate?.ToString("dd/MM/yyyy"))))
                .Concat(unproven.Select(x=>new BankEvidenceReviewDto($"UNPROVEN-{x.Id}",x.Amount,"Chưa có chứng nhận khoản thu riêng biệt; chưa cộng vào quỹ",x.BankBusinessDate?.ToString("dd/MM/yyyy"))))
                .Concat(conflictingEvidence.Select(x=>new BankEvidenceReviewDto($"CONFLICT-{x.Id}",x.ObservedAmount,"Chứng cứ mâu thuẫn với định danh cũ; toàn bộ số tiền này chờ đối soát, không cộng lần thu mới",null))).ToList();
            if(bankReviews.Count>0)warnings.Add($"Có {bankReviews.Count} bằng chứng chưa phân ngày hoặc tài khoản. Bằng chứng chưa có ngày được giữ trong nhóm đối soát cả khi tiếp nhận muộn; chưa cộng vào quỹ hay gán giờ nhận tiền giả.");
            if(bankBalanceIncomplete)warnings.Add("Chưa xác định số dư ngân hàng vì còn ngày, quỹ, định danh hoặc điều chỉnh cần đối soát. Giữ các khoản thực nhận cũ, chưa tự đảo tiền; điều kiện tính quỹ tiền mặt không thay đổi.");
            var refunds = await Bounded((from payment in db.SalesReturnPayments.AsNoTracking() join ret in db.SalesReturns.AsNoTracking() on payment.SalesReturnId equals ret.Id
                where payment.StoreId == StoreId && ret.StoreId == StoreId && ret.Status == SalesReturnStatus.Completed && payment.PaidAtUtc >= start && payment.PaidAtUtc < p.ToUtcExclusive
                select new { payment.Id, payment.PaidAtUtc, payment.Method, payment.Amount, payment.ReferenceCode, ret.ReturnNumber }), ct);
            flows.AddRange(refunds.Select(x => new MoneyMovementDto($"RETURN-{x.Id}", x.PaidAtUtc, Fund(x.Method), x.ReturnNumber, "Hoàn tiền trả hàng", -x.Amount, x.ReferenceCode)));
            var receipts = await Bounded(db.Set<CustomerDebtReceipt>().AsNoTracking().Where(x => x.StoreId == StoreId && x.CreatedAtUtc >= start && x.CreatedAtUtc < p.ToUtcExclusive)
                .Select(x => new { x.Id, x.ClientRequestId, x.CustomerId, x.POSShiftId, x.CreatedAtUtc, x.Method, x.Amount, x.Reference }), ct);
            flows.AddRange(receipts.Select(x => new MoneyMovementDto($"DEBT-{x.Id}", x.CreatedAtUtc, Fund(x.Method), $"Thu nợ khách #{x.CustomerId}", "Thu công nợ", x.Amount, x.Reference)));
            var deposits = await Bounded(db.Set<CustomerDepositEntry>().AsNoTracking().Where(x => x.StoreId == StoreId && (x.Kind == "Receive" || x.Kind == "Refund") && x.CreatedAtUtc >= start && x.CreatedAtUtc < p.ToUtcExclusive)
                .Select(x => new { x.Id, x.CustomerDepositId, x.CreatedAtUtc, x.Method, x.Amount, x.Kind, x.Reference }), ct);
            flows.AddRange(deposits.Select(x => new MoneyMovementDto($"DEPOSIT-{x.Id}", x.CreatedAtUtc, Fund(x.Method ?? PaymentMethod.Other), $"Cọc DC-{x.CustomerDepositId}", x.Kind == "Receive" ? "Nhận cọc" : "Hoàn cọc", x.Amount, x.Reference)));
            var nativePayouts = db.Set<GaoApp.Domain.Storefront.StorefrontRefundEvidence>().Where(e => e.StoreId == StoreId && e.FundingId != null);
            var manual = await Bounded(db.TreasuryEntries.AsNoTracking().Where(x => x.StoreId == StoreId)
                .Select(x => new { x.Id, OccurredAtUtc = nativePayouts.Where(e => e.TreasuryEntryId == x.Id).Select(e => (DateTime?)e.ExecutedAtUtc).Max() ?? x.OccurredAtUtc,
                    x.Fund, x.TargetFund, x.Name, x.Amount, x.Reference, x.OperatingExpenseId, x.PurchasePayableId,
                    x.POSShiftCashTransactionId, x.IsVoucherLink, x.ReversalOfId, x.ReversedAtUtc, x.RowVersion })
                .Where(x => x.OccurredAtUtc >= start && x.OccurredAtUtc < p.ToUtcExclusive), ct);
            var linked = manual.Where(x => x.POSShiftCashTransactionId != null).ToDictionary(x => x.POSShiftCashTransactionId!.Value);
            var cash = await Bounded(db.POSShiftCashTransactions.AsNoTracking().Where(x => x.StoreId == StoreId && x.CreatedAtUtc >= start && x.CreatedAtUtc < p.ToUtcExclusive), ct);
            var codCashIds = await db.Set<GaoApp.Domain.Storefront.StorefrontCodReceipt>().AsNoTracking().Where(x => x.StoreId == StoreId && x.POSShiftCashTransactionId != null)
                .Select(x => x.POSShiftCashTransactionId!.Value).ToListAsync(ct);
            var cashIds = cash.Select(x => x.Id).ToList(); var treasuryIds = manual.Select(x => x.Id).ToList();
            var refundClaims = await db.Set<GaoApp.Domain.Storefront.StorefrontRefundEvidence>().AsNoTracking().Where(x => x.StoreId == StoreId && x.FundingId != null &&
                (x.POSShiftCashTransactionId != null && cashIds.Contains(x.POSShiftCashTransactionId.Value) || x.TreasuryEntryId != null && treasuryIds.Contains(x.TreasuryEntryId.Value)))
                .Select(x => new { x.POSShiftCashTransactionId, x.TreasuryEntryId }).ToListAsync(ct);
            var refundCashIds = refundClaims.Where(x => x.POSShiftCashTransactionId.HasValue).Select(x => x.POSShiftCashTransactionId!.Value).ToHashSet();
            var refundTreasuryIds = refundClaims.Where(x => x.TreasuryEntryId.HasValue).Select(x => x.TreasuryEntryId!.Value).ToHashSet();
            var receiptIds = receipts.Select(x => x.Id).ToHashSet();
            var receiptRequests = receipts.ToDictionary(x => x.ClientRequestId);
            foreach (var x in cash) {
                // Deposit mirrors carry a durable FK; older debt mirrors carry the exact receipt request ID.
                if (x.CustomerDepositEntryId != null || x.CustomerDebtReceiptId is int receiptId && receiptIds.Contains(receiptId)) continue;
                if (x.Reason == "Thu công nợ khách hàng" && x.Note != null && Guid.TryParse(x.Note.Split("; mã thu ").Last(), out var requestId)
                    && receiptRequests.TryGetValue(requestId, out var receipt) && receipt.POSShiftId == x.POSShiftId && receipt.Amount == x.Amount) continue;
                linked.TryGetValue(x.Id, out var link);
                flows.Add(new($"CASH-{x.Id}", x.CreatedAtUtc, "cash", link?.Name ?? x.Reason, refundCashIds.Contains(x.Id) ? "Thực hoàn website · phiếu chi gốc" : codCashIds.Contains(x.Id) ? "Thực nộp COD website" : link == null ? "Phiếu tiền mặt ca" : "Liên kết phiếu ca",
                    x.Type == POSShiftCashTransactionType.CashIn ? x.Amount : -x.Amount, link?.Reference ?? $"CA-{x.POSShiftId}", link?.Id, link == null ? null : Version(link.RowVersion), link != null && link.ReversedAtUtc == null && !refundCashIds.Contains(x.Id), link?.TargetFund != null));
                if (link?.TargetFund != null) flows.Add(new($"CASH-{x.Id}-TO", x.CreatedAtUtc, link.TargetFund, link.Name, "Chuyển quỹ từ phiếu ca", -link.Amount, link.Reference, IsTransfer: true));
            }
            foreach (var x in manual.Where(x => !x.IsVoucherLink)) {
                var source = refundTreasuryIds.Contains(x.Id) ? "Thực hoàn website · phiếu quỹ gốc" : x.ReversalOfId != null ? "Đảo phiếu" : x.TargetFund != null ? "Chuyển quỹ" : x.OperatingExpenseId != null ? "Chi phí vận hành" : x.PurchasePayableId != null ? "Thanh toán nhà cung cấp" : "Thu chi ngoài bán hàng";
                var at = refundTreasuryIds.Contains(x.Id) ? DateTime.SpecifyKind(x.OccurredAtUtc, DateTimeKind.Utc) : x.OccurredAtUtc;
                flows.Add(new($"TC-{x.Id}", at, x.Fund, x.Name, source, x.Amount, x.Reference, x.Id, Version(x.RowVersion), x.ReversalOfId == null && x.ReversedAtUtc == null && !refundTreasuryIds.Contains(x.Id), x.TargetFund != null));
                if (x.TargetFund != null) flows.Add(new($"TC-{x.Id}-TO", x.OccurredAtUtc, x.TargetFund, x.Name, source, -x.Amount, x.Reference, IsTransfer: true));
            }
            var unlinkedPaidExpenses = await db.OperatingExpenses.CountAsync(x => x.StoreId == StoreId && x.IsPaid && !db.TreasuryEntries.Any(y => y.StoreId == StoreId && y.OperatingExpenseId == x.Id) &&
                !db.Set<GaoApp.Domain.Storefront.StorefrontCarrierWithholding>().Any(y => y.StoreId == StoreId && y.OperatingExpenseId == x.Id && y.Amount == x.Amount), ct);
            if (unlinkedPaidExpenses > 0) warnings.Add($"{unlinkedPaidExpenses} chi phí cũ chỉ có dấu đã thanh toán, chưa có ngày và phiếu thu chi; chưa cộng vào dòng tiền.");
            var unlinkedSupplier = await db.Set<PurchasePayable>().CountAsync(x => x.StoreId == StoreId && x.Status == PurchasePayableStatus.Paid && x.PaidAtUtc >= start && x.PaidAtUtc < p.ToUtcExclusive
                && !db.TreasuryEntries.Any(y => y.StoreId == StoreId && y.PurchasePayableId == x.Id), ct);
            if (unlinkedSupplier > 0) warnings.Add($"{unlinkedSupplier} khoản nhà cung cấp đã trả theo chứng từ nhập nhưng thiếu phương thức thanh toán; chưa cộng vào tiền mặt/chuyển khoản.");
            var voidCount = await db.Orders.CountAsync(x => x.StoreId == StoreId && x.Status == OrderStatus.Voided && x.CompletedAtUtc >= start && x.CompletedAtUtc < p.ToUtcExclusive, ct);
            if (voidCount > 0) warnings.Add($"{voidCount} đơn đã hủy được loại khỏi thu bán hàng theo trạng thái hiện tại. Cần đối chiếu phiếu hoàn tiền thực tế của đơn hủy.");
            var selected = flows.Where(x => query.Fund is null or "" or "all" || x.Fund == query.Fund).ToList();
            var unclassified = flows.Count(x => x.Source == "Phiếu tiền mặt ca");
            if (unclassified > 0) warnings.Add($"{unclassified} phiếu tiền mặt ca chưa phân loại thu/chi bên ngoài hay luân chuyển nội bộ. Liên kết/phân loại để đối chiếu số dư; chưa cộng các phiếu này vào chỉ số thực thu/chi và biểu đồ.");
            var options = new[] { "cash", "bank", "other" }.Select(x => new ReportOptionDto(x, FundName(x))).ToList();
            var summary = options.Where(x => query.Fund is null or "" or "all" || x.Id == query.Fund).Select(f => {
                var baseline = openings.SingleOrDefault(x => x.Fund == f.Id);
                var fundRows = selected.Where(x => x.Fund == f.Id).ToList();
                var current = fundRows.Where(x => x.AtUtc >= p.FromUtc).ToList();
                decimal? opening = baseline != null && baseline.AsOfDate <= p.FromDate && unlinkedSupplier == 0 && unlinkedPaidExpenses == 0 && voidCount == 0 && (f.Id != "cash" || unclassified == 0) && (f.Id!="bank"||!bankBalanceIncomplete)
                    ? baseline.Amount + fundRows.Where(x => x.AtUtc >= baseline.AsOfDate.AddHours(-7) && x.AtUtc < p.FromUtc).Sum(x => x.Amount) : null;
                var incoming = current.Where(x => x.Amount > 0).Sum(x => x.Amount); var outgoing = -current.Where(x => x.Amount < 0).Sum(x => x.Amount);
                return new FundSummaryDto(f.Id, f.Name, opening, incoming, outgoing, incoming - outgoing, opening + incoming - outgoing, baseline?.AsOfDate);
            }).ToList();
            if (summary.Any(x => x.Opening == null && (x.Fund != "other" || x.Inflow != 0 || x.Outflow != 0))) warnings.Add("Số dư chỉ xuất hiện khi quỹ có số dư ban đầu và dữ liệu thanh toán đủ căn cứ. Phát sinh thu chi vẫn hiển thị để đối chiếu.");
            var items = selected.Where(x => x.AtUtc >= p.FromUtc).OrderByDescending(x => x.AtUtc).ThenBy(x => x.Id).ToList();
            var trend = Enumerable.Range(0, p.BucketCount).Select(i => {
                var bucket = items.Where(x => !x.IsTransfer && x.Source != "Phiếu tiền mặt ca" && Bucket(p, x.AtUtc) == i).ToList();
                return new CashTrendDto(periods.FormatBucketLabel(p, i), bucket.Where(x => x.Amount > 0).Sum(x => x.Amount), -bucket.Where(x => x.Amount < 0).Sum(x => x.Amount), bucket.Sum(x => x.Amount));
            }).ToList();
            if(items.Any(x=>x.TimePrecision==BankTimePrecision.BankDay))
            {
                warnings.Add("Biểu đồ thu/chi được nhóm theo ngày vì có bằng chứng ngân hàng chỉ chính xác đến ngày; không gán khoản này vào một giờ giả.");
                trend=Enumerable.Range(0,(p.ToDate.Date-p.FromDate.Date).Days+1).Select(offset=>{
                    var day=p.FromDate.Date.AddDays(offset);var rows=items.Where(x=>!x.IsTransfer&&x.Source!="Phiếu tiền mặt ca"&&x.AtUtc.AddHours(7).Date==day).ToList();
                    return new CashTrendDto(day.ToString("dd/MM/yyyy"),rows.Where(x=>x.Amount>0).Sum(x=>x.Amount),-rows.Where(x=>x.Amount<0).Sum(x=>x.Amount),rows.Sum(x=>x.Amount));
                }).ToList();
            }
            var difference = await db.POSShifts.Where(x => x.StoreId == StoreId && x.ClosedAtUtc >= p.FromUtc && x.ClosedAtUtc < p.ToUtcExclusive && x.ClosingCashActual != null)
                .SumAsync(x => x.ClosingCashActual!.Value - x.ClosingCashExpected, ct);
            return new CashFlowReportDto(p, options, summary, trend, items, items.Count, warnings, difference,bankReviews);
        }, ct);
    }
}
