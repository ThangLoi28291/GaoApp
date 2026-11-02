using System.Globalization;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.Acb;

public sealed record BankNotificationAdminWait(int QrId, int OrderId, int TerminalId, string TerminalName,
    int BankAccountId, decimal Amount, string RequestCode, DateTime CreatedAtUtc, bool Approved);

public sealed partial class BankNotificationBridgeService
{
    internal const string AdminApprovalSource = "AdminApprovalPending";
    internal const string AdminReceiptApprovalSource = "NotificationAdminPending";
    internal const string AdminCompletedSource = "AdminManualConfirmed";
    internal const string AdminReceiptCompletedSource = "NotificationAdmin";

    public async Task<IReadOnlyList<BankNotificationAdminWait>> AdminWaitingAsync(CancellationToken ct)
    {
        var rows = await (from q in db.PosPaymentQrRequests.AsNoTracking()
                          join o in db.Orders.AsNoTracking() on q.OrderId equals o.Id
                          join s in db.POSShifts.AsNoTracking() on o.POSShiftId equals s.Id
                          join b in db.StoreBankAccounts.AsNoTracking() on q.BankAccountId equals b.Id
                          join t in db.POSTerminals.AsNoTracking() on s.TerminalId equals t.Id
                          where q.StoreId == StoreId && o.StoreId == StoreId && s.StoreId == StoreId && b.StoreId == StoreId && t.StoreId == StoreId &&
                              q.Status == PosPaymentQrStatus.Pending && q.PaymentId == null && o.Status == OrderStatus.Draft &&
                              s.Status == POSShiftStatus.Open && (s.CurrentOrderId == null || s.CurrentOrderId == o.Id) &&
                              q.ConfirmMode == BankQrConfirmMode.Manual && q.QrRenderMode == BankQrRenderMode.LocalEmvQr && b.BankCode == "ACB" && b.IsActive
                          orderby q.CreatedAtUtc descending
                          select new { q, s.TerminalId, t.Name }).Take(100).ToListAsync(ct);
        return rows.Select(x => new BankNotificationAdminWait(x.q.Id, x.q.OrderId, x.TerminalId, x.Name,
            x.q.BankAccountId, x.q.Amount, x.q.RequestCode, x.q.CreatedAtUtc,
            x.q.NotificationConfirmationSource is AdminApprovalSource or AdminReceiptApprovalSource)).ToArray();
    }

    public async Task<object> AdminApproveAsync(int qrId, decimal amount, int? receiptId, CancellationToken ct)
    {
        if (runtime.StoreId != StoreId || runtime.UserId is not > 0)
            throw new InvalidOperationException("Chưa xác định người xác nhận của cửa hàng.");
        await using var gate = await locks.AcquireAsync(db, 0, ct);
        var hint = await db.PosPaymentQrRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == qrId && x.StoreId == StoreId, ct)
            ?? throw new InvalidOperationException("Không tìm thấy khoản chuyển khoản.");
        await using var orderGate = await locks.AcquireAsync(db, hint.OrderId, ct);
        var qr = await db.PosPaymentQrRequests.Include(x => x.BankAccount).SingleAsync(x => x.Id == qrId && x.StoreId == StoreId, ct);
        await db.Entry(qr).ReloadAsync(ct);
        if (qr.Amount != amount || amount <= 0) throw new InvalidOperationException("Số tiền không khớp khoản chuyển khoản đang chờ.");
        if (qr.PaymentId != null)
        {
            if (receiptId.HasValue && qr.NotificationReceiptId != receiptId)
                await LinkRecordedAdminReceiptAsync(qr, receiptId.Value, ct);
            return new { qrId, approved = true, recorded = true };
        }
        if (qr.Status != PosPaymentQrStatus.Pending || qr.ConfirmMode != BankQrConfirmMode.Manual || qr.QrRenderMode != BankQrRenderMode.LocalEmvQr ||
            qr.BankAccount.StoreId != StoreId || qr.BankAccount.BankCode != "ACB" || !qr.BankAccount.IsActive)
            throw new InvalidOperationException("QR đã kết thúc hoặc không thuộc luồng thông báo ACB.");
        var order = await db.Orders.Include(x => x.POSShift).Include(x => x.Payments).Include(x => x.Lines).ThenInclude(x => x.Variant)
            .SingleAsync(x => x.StoreId == StoreId && x.Id == qr.OrderId, ct);
        if (order.Status != OrderStatus.Draft || order.POSShift.StoreId != StoreId || order.POSShift.Status != POSShiftStatus.Open ||
            order.POSShift.CurrentOrderId != null && order.POSShift.CurrentOrderId != order.Id || AcbPaymentPolicy.Balance(order) <= 0)
            throw new InvalidOperationException("Đơn không còn đang chờ tại ca mở. Tải lại danh sách để kiểm tra.");
        var wait = await db.Set<BankNotificationWait>().SingleOrDefaultAsync(x => x.StoreId == StoreId && x.QrId == qrId, ct);
        var fingerprint = BankNotificationParser.AccountFingerprint(qr.BankAccount);
        if (wait != null && (wait.OrderId != order.Id || wait.ShiftId != order.POSShiftId || wait.TerminalId != order.POSShift.TerminalId ||
            wait.BankAccountId != qr.BankAccountId || wait.Amount != amount || wait.BankAccountFingerprint != fingerprint ||
            !AcbPaymentPolicy.MatchesFingerprint(order, wait.CartFingerprint)))
            throw new InvalidOperationException("Đơn hoặc tài khoản nhận đã thay đổi từ khi chờ chuyển khoản.");
        BankNotificationReceipt? receipt = null;
        if (receiptId.HasValue)
        {
            receipt = await db.Set<BankNotificationReceipt>().SingleOrDefaultAsync(x => x.StoreId == StoreId && x.Id == receiptId.Value, ct)
                ?? throw new InvalidOperationException("Không tìm thấy thông báo.");
            await db.Entry(receipt).ReloadAsync(ct);
            if (await BankNotificationEvidence.ClaimedAsync(db, receipt, wait?.Id, ct))
                throw new InvalidOperationException("Khoản tiền đã được xác nhận hoặc dành cho một đơn khác.");
            var original = BankNotificationParser.Parse(receipt.Content);
            var orderId = original?.OrderId ?? receipt.OrderId;
            var qrCode = original?.QrCode ?? receipt.QrCode;
            var occurred = BankNotificationEvidence.OccurredAt(receipt);
            var source = await db.Set<BankNotificationDevice>().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == StoreId && x.Id == receipt.DeviceId, ct);
            if (receipt.BankAccountId != qr.BankAccountId || receipt.Amount != amount || source?.BankAccountFingerprint != fingerprint ||
                orderId != null && orderId != order.Id || qrCode != null && !string.Equals(qrCode, qr.RequestCode, StringComparison.OrdinalIgnoreCase) ||
                occurred < qr.CreatedAtUtc.AddSeconds(-5) || occurred > qr.ExpireAtUtc.AddSeconds(5) ||
                receipt.PaymentId != null || receipt.AppliedWaitId != null && receipt.AppliedWaitId != wait?.Id)
                throw new InvalidOperationException("Thông báo không khớp QR, số tiền, tài khoản hoặc đã dùng cho khoản khác.");
        }
        if (qr.NotificationConfirmationSource is AdminApprovalSource or AdminReceiptApprovalSource)
        {
            if (receiptId != null && qr.NotificationReceiptId != receiptId) throw new InvalidOperationException("Khoản này đã có lệnh xác nhận khác từ admin.");
            return new { qrId, approved = true, recorded = false };
        }
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (wait == null)
        {
            wait = new BankNotificationWait { StoreId = StoreId, QrId = qrId, OrderId = order.Id, BankAccountId = qr.BankAccountId,
                BankAccountFingerprint = fingerprint, TerminalId = order.POSShift.TerminalId, ShiftId = order.POSShiftId, Amount = amount,
                ExpiresAtUtc = qr.ExpireAtUtc, CartFingerprint = AcbPaymentPolicy.Fingerprint(order) };
            db.Add(wait); await db.SaveChangesAsync(ct);
        }
        // The waiting-row action has no receipt id. Reuse existing, unambiguous
        // bank evidence for this QR instead of leaving a second history entry.
        if (receipt == null)
        {
            var matched = await BankNotificationAdminLink.FindReceiptAsync(db, qr, ct);
            if (matched != null)
            {
                receipt = await db.Set<BankNotificationReceipt>().SingleAsync(r => r.StoreId == StoreId && r.Id == matched.Id, ct);
                await db.Entry(receipt).ReloadAsync(ct);
            }
        }
        qr.NotificationReceiptId = receipt?.Id;
        qr.NotificationConfirmationSource = receipt == null ? AdminApprovalSource : AdminReceiptApprovalSource;
        qr.ManualConfirmedByUserId = runtime.UserId;
        qr.ManualConfirmedAtUtc = DateTime.UtcNow;
        if (receipt != null)
        {
            receipt.AppliedWaitId = wait.Id;
            receipt.ConfirmedByUserId = runtime.UserId;
            receipt.ConfirmedAtUtc = DateTime.UtcNow;
            receipt.ConfirmationSource = AdminReceiptApprovalSource;
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        // The persisted command survives a disconnected POS. Money/finalization is
        // processed by the original terminal, using its normal checkout/print path.
        await notifier.NotifyTerminalAsync(StoreId, wait.TerminalId.ToString(CultureInfo.InvariantCulture), "bank_notification_available", order.Id, ct: ct);
        return new { qrId, approved = true, recorded = false };
    }

    public async Task<int[]> AdminApprovalsAsync(CancellationToken ct) => await (
        from q in db.PosPaymentQrRequests.AsNoTracking()
        join o in db.Orders.AsNoTracking() on q.OrderId equals o.Id
        join s in db.POSShifts.AsNoTracking() on o.POSShiftId equals s.Id
        where q.StoreId == StoreId && o.StoreId == StoreId && s.StoreId == StoreId && s.TerminalId == runtime.TerminalId &&
            s.Status == POSShiftStatus.Open && o.Status == OrderStatus.Draft && (s.CurrentOrderId == null || s.CurrentOrderId == o.Id) &&
            (q.Status == PosPaymentQrStatus.Pending && q.PaymentId == null || q.Status == PosPaymentQrStatus.ManualConfirmed && q.PaymentId != null) &&
            (q.NotificationConfirmationSource == AdminApprovalSource || q.NotificationConfirmationSource == AdminReceiptApprovalSource)
        select q.Id).Take(20).ToArrayAsync(ct);

    public async Task<object> CompleteAdminApprovalAsync(int qrId, CancellationToken ct)
    {
        await using var gate = await locks.AcquireAsync(db, 0, ct);
        var qr = await db.PosPaymentQrRequests.SingleOrDefaultAsync(x => x.Id == qrId && x.StoreId == StoreId, ct)
            ?? throw new InvalidOperationException("Không tìm thấy QR.");
        await db.Entry(qr).ReloadAsync(ct);
        if (qr.NotificationConfirmationSource is not (AdminApprovalSource or AdminReceiptApprovalSource or AdminCompletedSource or AdminReceiptCompletedSource))
            throw new InvalidOperationException("QR chưa được admin xác nhận.");
        if (BankNotificationAdminLink.WithoutReceipt(qr))
        {
            var candidate = await BankNotificationAdminLink.FindReceiptAsync(db, qr, ct);
            if (candidate != null) await LinkAdminReceiptAsync(candidate, ct);
        }
        BankNotificationReceipt? receipt = null;
        var wait = await db.Set<BankNotificationWait>().SingleAsync(x => x.StoreId == StoreId && x.QrId == qrId, ct);
        if (qr.NotificationReceiptId.HasValue)
            receipt = await db.Set<BankNotificationReceipt>().SingleAsync(x => x.StoreId == StoreId && x.Id == qr.NotificationReceiptId, ct);
        var result = await payments.ConfirmManualQrAsync(qrId, ct, receipt, wait);
        qr.NotificationConfirmationSource = receipt == null ? AdminCompletedSource : AdminReceiptCompletedSource;
        await db.SaveChangesAsync(ct);
        return result;
    }
}
