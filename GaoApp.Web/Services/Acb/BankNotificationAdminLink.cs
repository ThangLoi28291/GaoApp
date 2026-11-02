using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.Acb;

/// <summary>Attach phone evidence to an admin's existing acknowledgement, never create a second payment.</summary>
internal static class BankNotificationAdminLink
{
    internal sealed record Target(PosPaymentQrRequest Qr, BankNotificationWait Wait);

    internal static bool WithoutReceipt(PosPaymentQrRequest qr) => qr.NotificationReceiptId == null &&
        qr.NotificationConfirmationSource is BankNotificationBridgeService.AdminApprovalSource or BankNotificationBridgeService.AdminCompletedSource;

    internal static async Task<Target?> MatchAsync(AppDbContext db, BankNotificationReceipt receipt, CancellationToken ct)
    {
        var parsed = BankNotificationParser.Parse(receipt.Content);
        var orderId = parsed?.OrderId ?? receipt.OrderId;
        var code = parsed?.QrCode ?? receipt.QrCode;
        // Amount alone cannot identify which admin acknowledgement the phone is reporting.
        if (orderId is not > 0 && code == null) return null;
        var occurred = BankNotificationEvidence.OccurredAt(receipt);
        var qrs = db.PosPaymentQrRequests.AsNoTracking().Where(q => q.StoreId == receipt.StoreId && (orderId == null || q.OrderId == orderId) &&
            q.BankAccountId == receipt.BankAccountId && q.Amount == receipt.Amount && q.ConfirmMode == BankQrConfirmMode.Manual &&
            q.QrRenderMode == BankQrRenderMode.LocalEmvQr && q.CreatedAtUtc <= occurred.AddSeconds(5) && q.ExpireAtUtc >= occurred.AddSeconds(-5));
        if (code != null) { var normalized = code.ToUpperInvariant(); qrs = qrs.Where(q => q.RequestCode.ToUpper() == normalized); }
        // Include older requests when testing ambiguity: a POS without a QR id must not
        // attach to an arbitrary installment merely because that installment was approved.
        var matches = await qrs.Take(2).ToListAsync(ct);
        if (matches.Count != 1) return null;
        var qr = matches[0];
        if (qr.Status is not (PosPaymentQrStatus.Pending or PosPaymentQrStatus.ManualConfirmed)) return null;
        var wait = await db.Set<BankNotificationWait>().AsNoTracking().SingleOrDefaultAsync(w => w.StoreId == receipt.StoreId && w.QrId == qr.Id, ct);
        var source = await db.Set<BankNotificationDevice>().IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(d => d.StoreId == receipt.StoreId && d.Id == receipt.DeviceId, ct);
        if (wait == null || wait.OrderId != qr.OrderId || wait.BankAccountId != qr.BankAccountId || wait.Amount != qr.Amount ||
            source == null || string.IsNullOrEmpty(source.BankAccountFingerprint) || source.BankAccountFingerprint != wait.BankAccountFingerprint) return null;
        return new(qr, wait);
    }

    internal static async Task<BankNotificationReceipt?> FindReceiptAsync(AppDbContext db, PosPaymentQrRequest qr, CancellationToken ct)
    {
        var orderToken = qr.OrderId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var requestCode = qr.RequestCode.ToUpperInvariant();
        var receipts = await db.Set<BankNotificationReceipt>().AsNoTracking().Where(r => r.StoreId == qr.StoreId &&
            r.BankAccountId == qr.BankAccountId && r.Amount == qr.Amount && r.PaymentId == null && r.AppliedWaitId == null &&
            (r.OrderId == qr.OrderId || r.OrderId == null && (r.Content.Contains(orderToken) || r.QrCode != null && r.QrCode.ToUpper() == requestCode)))
            .OrderBy(r => r.Id).ToListAsync(ct);
        var matches = new List<BankNotificationReceipt>();
        foreach (var receipt in receipts)
            if ((await MatchAsync(db, receipt, ct))?.Qr.Id == qr.Id) matches.Add(receipt);
        if (matches.Count == 0 || matches.Any(r => !BankNotificationEvidence.SameTransfer(matches[0], r))) return null;
        var first = matches[0];
        return await BankNotificationEvidence.ClaimedAsync(db, first, null, ct) ? null : first;
    }

    internal static async Task<bool> ValidPaymentAsync(AppDbContext db, PosPaymentQrRequest qr, CancellationToken ct) =>
        qr.PaymentId == null ? qr.Status == PosPaymentQrStatus.Pending : qr.Status == PosPaymentQrStatus.ManualConfirmed &&
        await db.OrderPayments.AsNoTracking().AnyAsync(p => p.StoreId == qr.StoreId && p.Id == qr.PaymentId && p.OrderId == qr.OrderId &&
            p.Method == PaymentMethod.BankTransfer && p.Amount == qr.Amount && p.ReferenceCode == qr.RequestCode, ct);

    internal static void Attach(PosPaymentQrRequest qr, BankNotificationWait wait, BankNotificationReceipt receipt)
    {
        var commandPending = qr.NotificationConfirmationSource is BankNotificationBridgeService.AdminApprovalSource or BankNotificationBridgeService.AdminReceiptApprovalSource;
        qr.NotificationReceiptId = receipt.Id;
        qr.NotificationConfirmationSource = commandPending ? BankNotificationBridgeService.AdminReceiptApprovalSource : BankNotificationBridgeService.AdminReceiptCompletedSource;
        receipt.AppliedWaitId = wait.Id;
        receipt.PaymentId = qr.PaymentId;
        receipt.ConfirmationSource = qr.PaymentId == null ? BankNotificationBridgeService.AdminReceiptApprovalSource : BankNotificationBridgeService.AdminReceiptCompletedSource;
        receipt.ConfirmedByUserId = qr.ManualConfirmedByUserId;
        receipt.ConfirmedAtUtc = qr.ManualConfirmedAtUtc;
    }
}

public sealed partial class BankNotificationBridgeService
{
    private async Task LinkRecordedAdminReceiptAsync(PosPaymentQrRequest qr, int receiptId, CancellationToken ct)
    {
        if (!BankNotificationAdminLink.WithoutReceipt(qr) || !await BankNotificationAdminLink.ValidPaymentAsync(db, qr, ct))
            throw new InvalidOperationException("Khoản chuyển khoản đã được ghi nhận bằng giao dịch khác.");
        var receipt = await db.Set<BankNotificationReceipt>().SingleOrDefaultAsync(r => r.StoreId == StoreId && r.Id == receiptId, ct)
            ?? throw new InvalidOperationException("Không tìm thấy thông báo.");
        await db.Entry(receipt).ReloadAsync(ct);
        var wait = await db.Set<BankNotificationWait>().AsNoTracking().SingleOrDefaultAsync(w => w.StoreId == StoreId && w.QrId == qr.Id, ct);
        var source = await db.Set<BankNotificationDevice>().IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(d => d.StoreId == StoreId && d.Id == receipt.DeviceId, ct);
        var parsed = BankNotificationParser.Parse(receipt.Content);
        var orderId = parsed?.OrderId ?? receipt.OrderId;
        var code = parsed?.QrCode ?? receipt.QrCode;
        var occurred = BankNotificationEvidence.OccurredAt(receipt);
        if (wait == null || wait.OrderId != qr.OrderId || wait.BankAccountId != qr.BankAccountId || wait.Amount != qr.Amount ||
            source == null || string.IsNullOrEmpty(source.BankAccountFingerprint) || source.BankAccountFingerprint != wait.BankAccountFingerprint ||
            receipt.BankAccountId != qr.BankAccountId || receipt.Amount != qr.Amount || orderId != null && orderId != qr.OrderId ||
            code != null && !string.Equals(code, qr.RequestCode, StringComparison.OrdinalIgnoreCase) ||
            occurred < qr.CreatedAtUtc.AddSeconds(-5) || occurred > qr.ExpireAtUtc.AddSeconds(5) ||
            await BankNotificationEvidence.ClaimedAsync(db, receipt, null, ct))
            throw new InvalidOperationException("Thông báo không khớp khoản admin đã xác nhận hoặc đã được sử dụng.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        BankNotificationAdminLink.Attach(qr, wait, receipt);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    // Caller owns the store evidence lock. Only audit links change here; the POS
    // remains responsible for applying the existing admin command exactly once.
    private async Task<bool> LinkAdminReceiptAsync(BankNotificationReceipt receipt, CancellationToken ct)
    {
        var target = await BankNotificationAdminLink.MatchAsync(db, receipt, ct);
        if (target == null || !BankNotificationAdminLink.WithoutReceipt(target.Qr)) return false;
        await using var orderGate = await locks.AcquireAsync(db, target.Qr.OrderId, ct);
        var qr = await db.PosPaymentQrRequests.SingleAsync(q => q.StoreId == StoreId && q.Id == target.Qr.Id, ct);
        await db.Entry(qr).ReloadAsync(ct);
        if (!BankNotificationAdminLink.WithoutReceipt(qr) || !await BankNotificationAdminLink.ValidPaymentAsync(db, qr, ct)) return false;
        var candidate = await BankNotificationAdminLink.FindReceiptAsync(db, qr, ct);
        if (candidate == null) return false;
        var linked = await db.Set<BankNotificationReceipt>().SingleAsync(r => r.StoreId == StoreId && r.Id == candidate.Id, ct);
        await db.Entry(linked).ReloadAsync(ct);
        if (linked.PaymentId != null || linked.AppliedWaitId != null) return false;
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        BankNotificationAdminLink.Attach(qr, target.Wait, linked);
        await db.SaveChangesAsync(ct); if (tx is not null) await tx.CommitAsync(ct);
        return true;
    }
}
