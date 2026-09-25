using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.Acb;

public sealed class AcbReconciliationService(AppDbContext db)
{
    private int StoreId => db.CurrentStoreId is > 0 ? db.CurrentStoreId.Value : throw new InvalidOperationException("Chưa xác định cửa hàng.");

    public async Task<AcbReconciliationPage> ReadAsync(DateTime? from, DateTime? to, string? source, int page, CancellationToken ct)
    {
        var start = (from ?? DateTime.UtcNow.AddHours(7).AddDays(-1)).Date;
        var end = (to ?? start).Date;
        if (end < start || (end - start).TotalDays > 30) throw new ArgumentException("Chọn khoảng tra cứu tối đa 31 ngày.");
        source ??= "TRANSACTION_HISTORY";
        if (source is not ("ALL" or "TRANSACTION_HISTORY" or "TRANSACTION_UPDATE")) throw new ArgumentException("Loại thông báo không hợp lệ.");
        page = Math.Max(1, page);
        var query = db.Set<AcbQrNotificationItem>().AsNoTracking().Where(x => x.StoreId == StoreId &&
            x.BusinessDate >= start && x.BusinessDate <= end && (source == "ALL" || x.RequestCode == source));
        var total = await query.CountAsync(ct);
        const int size = 50;
        page = Math.Min(page, Math.Max(1, (total + size - 1) / size));
        var entries = await query.Include(x => x.Receipt).OrderByDescending(x => x.BusinessDate).ThenByDescending(x => x.Id)
            .Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var references = entries.Select(x => x.ProviderOrderId).Where(x => x.Length > 0).Distinct().ToList();
        var sessions = await db.Set<AcbQrSession>().AsNoTracking().Where(x => x.StoreId == StoreId && references.Contains(x.ProviderOrderId)).ToListAsync(ct);
        var sessionIds = sessions.Select(x => x.Id).ToList();
        var orderIds = sessions.Select(x => x.OrderId).Distinct().ToList();
        var orders = await db.Orders.AsNoTracking().Include(x => x.Payments).Where(x => x.StoreId == StoreId && orderIds.Contains(x.Id)).ToListAsync(ct);
        var transactions = await db.Set<AcbPaymentTransaction>().AsNoTracking().Where(x => x.StoreId == StoreId && sessionIds.Contains(x.SessionId)).ToListAsync(ct);
        var rows = entries.Select(item =>
        {
            var session = sessions.SingleOrDefault(x => x.ProviderOrderId == item.ProviderOrderId);
            var order = orders.SingleOrDefault(x => x.Id == session?.OrderId);
            var paid = transactions.Where(x => x.SessionId == session?.Id && AcbProtocol.IsPaid(x.Status)).ToList();
            var payment = order?.Payments.SingleOrDefault(x => !x.IsDeleted && x.Id == session?.PaymentId && x.Method == PaymentMethod.BankTransfer);
            var (state, reason) = Evaluate(item, session, order, paid, payment);
            return new AcbReconciliationRow(item.Id, item.ReceiptId, item.Receipt.Page, item.BusinessDate,
                item.ProviderOrderId, session?.OrderId, item.RequestCode, item.Amount, item.DebitOrCredit,
                item.Content, paid.Count == 0 ? null : paid.Sum(x => x.Amount), payment?.Amount,
                state, reason, item.Receipt.CreatedAtUtc);
        }).ToList();
        // Include every received page of a relevant batch, including pages covering other dates.
        var relevantIds = query.Select(x => x.Receipt.ClientRequestId);
        var receivedFrom = start.AddHours(-7);
        var receivedTo = end.AddDays(2).AddHours(-7);
        var batches = await db.Set<AcbCallbackReceipt>().AsNoTracking().Where(x => x.StoreId == StoreId &&
            (source == "ALL" || x.RequestCode == source) && (relevantIds.Contains(x.ClientRequestId) ||
                (!x.Items.Any() && x.CreatedAtUtc >= receivedFrom && x.CreatedAtUtc < receivedTo)))
            .GroupBy(x => new { x.RequestCode, x.ClientRequestId })
            .Select(g => new { g.Key.RequestCode, g.Key.ClientRequestId,
                ReceivedPages = g.Count(), TotalPages = g.Max(x => x.TotalPages), ProcessedPages = g.Count(x => x.ProcessedAtUtc != null),
                ReviewPages = g.Count(x => x.NeedsReview), LastReceivedAtUtc = g.Max(x => x.CreatedAtUtc) })
            .OrderByDescending(x => x.LastReceivedAtUtc).Take(20).ToListAsync(ct);
        return new(start, end, source, page, size, total, rows,
            batches.Select(x => new AcbReconciliationBatch(x.RequestCode, x.ClientRequestId, x.ReceivedPages,
                x.TotalPages, x.ProcessedPages, x.ReviewPages, x.LastReceivedAtUtc)).ToList());
    }

    private static (string, string) Evaluate(AcbQrNotificationItem item, AcbQrSession? session, Order? order,
        IReadOnlyList<AcbPaymentTransaction> paid, OrderPayment? payment)
    {
        if (item.DebitOrCredit == "debit" || item.TransactionStatus == "ERRORCORRECTED")
            return ("Review", "Báo nợ hoặc giao dịch bị điều chỉnh/hủy. Cần đối chiếu.");
        if (session == null || order == null) return ("Unmatched", "Chưa ghép được mã QR với hóa đơn của cửa hàng.");
        if (session.Status == AcbSessionStatus.ReviewRequired) return ("Review", session.ReviewReason ?? "QR cần kiểm tra.");
        if (item.Amount != session.Amount || paid.Count > 1 || (paid.Count == 1 && paid[0].Amount != session.Amount))
            return ("Mismatch", "Số tiền hoặc số lần chuyển khác yêu cầu QR.");
        if (paid.Count == 0) return ("AwaitingBank", item.Receipt.LastErrorCode ?? "Chưa có bằng chứng từ API tra cứu ngân hàng.");
        if (payment == null || payment.Amount != session.Amount || payment.ReferenceCode != session.ProviderOrderId)
            return ("Unposted", "ACB đã xác nhận; khoản thu chưa ghi vào hóa đơn. Kiểm tra tại máy POS đã tạo QR.");
        if (order.Status != OrderStatus.Completed || session.Status != AcbSessionStatus.Completed)
            return ("Unfinalized", "Đã ghi nhận tiền; đơn chưa chốt hoàn tất tại POS.");
        return ("Matched", "Khớp thông báo, giao dịch ACB và khoản thu trong hóa đơn.");
    }
}

public sealed record AcbReconciliationRow(int Id, int ReceiptId, int ReceiptPage, DateTime BusinessDate,
    string ProviderOrderId, int? OrderId, string RequestCode, decimal NotifiedAmount, string DebitOrCredit,
    string Content, decimal? VerifiedAmount, decimal? PostedAmount, string State, string Reason, DateTime ReceivedAtUtc);
public sealed record AcbReconciliationBatch(string RequestCode, string ClientRequestId, int ReceivedPages,
    int TotalPages, int ProcessedPages, int ReviewPages, DateTime LastReceivedAtUtc);
public sealed record AcbReconciliationPage(DateTime From, DateTime To, string Source, int Page, int PageSize,
    int Total, IReadOnlyList<AcbReconciliationRow> Rows, IReadOnlyList<AcbReconciliationBatch> Batches);
