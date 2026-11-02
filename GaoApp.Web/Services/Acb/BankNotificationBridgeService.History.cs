using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.Acb;

public sealed record BankNotificationHistoryItem
{
    public int? ReceiptId { get; init; }
    public int? QrId { get; init; }
    public int BankAccountId { get; init; }
    public decimal Amount { get; init; }
    public int? OrderId { get; init; }
    public string? OrderNumber { get; set; }
    public string? TerminalName { get; set; }
    public string? QrCode { get; init; }
    public string? SenderName { get; init; }
    public string? TransactionCode { get; init; }
    public string? Content { get; init; }
    public DateTime PostedAtUtc { get; init; }
    public int? AppliedWaitId { get; init; }
    public int? PaymentId { get; init; }
    public string? ConfirmationSource { get; init; }
    public DateTime? ConfirmedAtUtc { get; init; }
    public int? ConfirmedByUserId { get; init; }
    public string? ConfirmedByName { get; set; }
    public PosPaymentQrStatus? QrStatus { get; init; }
    public bool AwaitingTerminal => PaymentId == null && QrStatus == PosPaymentQrStatus.Pending &&
        ConfirmationSource is "AdminApprovalPending" or "NotificationAdminPending";
}

public sealed partial class BankNotificationBridgeService
{
    public async Task<IReadOnlyList<BankNotificationHistoryItem>> AdminHistoryAsync(CancellationToken ct)
    {
        var receipts = await (from r in db.Set<BankNotificationReceipt>().AsNoTracking().Where(x => x.StoreId == StoreId)
                              join w in db.Set<BankNotificationWait>().AsNoTracking().Where(x => x.StoreId == StoreId)
                                  on r.AppliedWaitId equals (int?)w.Id into waits
                              from w in waits.DefaultIfEmpty()
                              join q in db.PosPaymentQrRequests.AsNoTracking().Where(x => x.StoreId == StoreId)
                                  on (int?)r.Id equals q.NotificationReceiptId into qrs
                              from q in qrs.DefaultIfEmpty()
                              orderby (r.ConfirmedAtUtc ?? r.PostedAtUtc) descending, r.Id descending
                              select new BankNotificationHistoryItem
                              {
                                  ReceiptId = r.Id, QrId = q == null ? null : q.Id, BankAccountId = r.BankAccountId, Amount = r.Amount,
                                  OrderId = w == null ? r.OrderId : w.OrderId, QrCode = r.QrCode ?? (q == null ? null : q.RequestCode),
                                  SenderName = r.SenderName, TransactionCode = r.TransactionCode, Content = r.Content,
                                  PostedAtUtc = r.PostedAtUtc, AppliedWaitId = r.AppliedWaitId, PaymentId = r.PaymentId,
                                  ConfirmationSource = r.PaymentId != null && r.ConfirmationSource == AdminReceiptApprovalSource
                                      ? AdminReceiptCompletedSource : r.ConfirmationSource,
                                  ConfirmedAtUtc = r.ConfirmedAtUtc, ConfirmedByUserId = r.ConfirmedByUserId,
                                  QrStatus = q == null ? null : q.Status
                              }).Take(40).ToListAsync(ct);
        // Admin fallback is already durably audited on the QR. Do not manufacture
        // a phone notification, sender or bank transaction when no upload exists.
        var admin = await db.PosPaymentQrRequests.AsNoTracking().Where(q => q.StoreId == StoreId && q.NotificationReceiptId == null &&
            (q.NotificationConfirmationSource == AdminApprovalSource || q.NotificationConfirmationSource == AdminCompletedSource))
            .OrderByDescending(q => q.ManualConfirmedAtUtc ?? q.CreatedAtUtc).ThenByDescending(q => q.Id)
            .Select(q => new BankNotificationHistoryItem
            {
                QrId = q.Id, BankAccountId = q.BankAccountId, Amount = q.Amount, OrderId = q.OrderId, QrCode = q.RequestCode,
                PostedAtUtc = q.ManualConfirmedAtUtc ?? q.CreatedAtUtc, PaymentId = q.PaymentId, QrStatus = q.Status,
                ConfirmationSource = q.PaymentId != null ? AdminCompletedSource : q.NotificationConfirmationSource,
                ConfirmedAtUtc = q.ManualConfirmedAtUtc, ConfirmedByUserId = q.ManualConfirmedByUserId
            }).Take(40).ToListAsync(ct);
        // Older versions left an admin acknowledgement and its late phone upload
        // unlinked. Present their proven identity once without editing money on GET.
        var mergedAdminIds = new HashSet<int>();
        var mergedReceipts = new List<BankNotificationHistoryItem>();
        var unlinkedIds = receipts.Where(r => r.QrId == null && r.PaymentId == null && r.AppliedWaitId == null)
            .Select(r => r.ReceiptId!.Value).ToArray();
        var originals = await db.Set<BankNotificationReceipt>().AsNoTracking().Where(r => r.StoreId == StoreId && unlinkedIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, ct);
        foreach (var row in receipts)
        {
            if (row.ReceiptId is not { } id || !originals.TryGetValue(id, out var original)) { mergedReceipts.Add(row); continue; }
            var target = await BankNotificationAdminLink.MatchAsync(db, original, ct);
            if (target == null || !BankNotificationAdminLink.WithoutReceipt(target.Qr) ||
                !await BankNotificationAdminLink.ValidPaymentAsync(db, target.Qr, ct)) { mergedReceipts.Add(row); continue; }
            var candidate = await BankNotificationAdminLink.FindReceiptAsync(db, target.Qr, ct);
            if (candidate == null || !BankNotificationEvidence.SameTransfer(candidate, original)) { mergedReceipts.Add(row); continue; }
            if (!mergedAdminIds.Add(target.Qr.Id)) continue;
            mergedReceipts.Add(row with {
                ReceiptId = candidate.Id, QrId = target.Qr.Id, OrderId = target.Qr.OrderId, QrCode = target.Qr.RequestCode,
                SenderName = candidate.SenderName, TransactionCode = candidate.TransactionCode, Content = candidate.Content,
                PostedAtUtc = candidate.PostedAtUtc, AppliedWaitId = target.Wait.Id, PaymentId = target.Qr.PaymentId,
                ConfirmationSource = target.Qr.PaymentId == null ? AdminReceiptApprovalSource : AdminReceiptCompletedSource,
                ConfirmedAtUtc = target.Qr.ManualConfirmedAtUtc, ConfirmedByUserId = target.Qr.ManualConfirmedByUserId,
                QrStatus = target.Qr.Status
            });
        }
        // A newly confirmed older transfer must remain visible after it leaves
        // the waiting table. Keep PostedAtUtc as the original bank time shown.
        var rows = mergedReceipts.Concat(admin.Where(a => !mergedAdminIds.Contains(a.QrId!.Value))).OrderByDescending(x => x.ConfirmedAtUtc ?? x.PostedAtUtc)
            .ThenByDescending(x => x.ReceiptId ?? x.QrId).Take(40).ToArray();
        var orderIds = rows.Where(x => x.OrderId.HasValue).Select(x => x.OrderId!.Value).Distinct().ToArray();
        var orders = await (from o in db.Orders.AsNoTracking().Where(x => x.StoreId == StoreId && orderIds.Contains(x.Id))
                            join s in db.POSShifts.AsNoTracking().Where(x => x.StoreId == StoreId) on o.POSShiftId equals s.Id into shifts
                            from s in shifts.DefaultIfEmpty()
                            join t in db.POSTerminals.AsNoTracking().Where(x => x.StoreId == StoreId)
                                on (s == null ? null : (int?)s.TerminalId) equals (int?)t.Id into terminals
                            from t in terminals.DefaultIfEmpty()
                            select new { o.Id, o.OrderNumber, TerminalName = t == null ? null : t.Name }).ToDictionaryAsync(x => x.Id, ct);
        var userIds = rows.Where(x => x.ConfirmedByUserId.HasValue).Select(x => x.ConfirmedByUserId!.Value).Distinct().ToArray();
        var users = await db.Users.AsNoTracking().Where(x => userIds.Contains(x.Id))
            .Select(x => new { x.Id, Name = x.FullName ?? x.UserName }).ToDictionaryAsync(x => x.Id, ct);
        foreach (var row in rows)
        {
            if (row.OrderId.HasValue && orders.TryGetValue(row.OrderId.Value, out var order))
            { row.OrderNumber = order.OrderNumber; row.TerminalName = order.TerminalName; }
            if (row.ConfirmedByUserId.HasValue && users.TryGetValue(row.ConfirmedByUserId.Value, out var user)) row.ConfirmedByName = user.Name;
        }
        return rows;
    }
}
