using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.Acb;

public sealed partial class BankNotificationBridgeService
{
    internal const string ManualReviewSource = "ManualReviewRequested";
    internal const string ManualCompletedSource = "ManualReviewConfirmed";
    private readonly BankNotificationMonitor deviceMonitor = monitor ?? new();
    private int WarningSeconds => Math.Clamp(configuration?.GetValue<int?>("BankNotifications:WarningAfterSeconds") ?? 60, 10, 600);
    private int ManualSeconds => Math.Clamp(configuration?.GetValue<int?>("BankNotifications:ManualAfterSeconds") ?? 120, WarningSeconds + 1, 3600);
    public async Task<object> WaitingStatusAsync(int qrId, CancellationToken ct)
    {
        var qr = await db.PosPaymentQrRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == qrId && x.StoreId == StoreId, ct)
            ?? throw new InvalidOperationException("Không tìm thấy QR.");
        var order = await RequireWaitingTerminalAsync(qr, ct);
        var devices = await db.Set<BankNotificationDevice>().AsNoTracking().Where(x => x.StoreId == StoreId && x.BankAccountId == qr.BankAccountId && x.Enabled).ToListAsync(ct);
        return new { qrId, orderId = qr.OrderId, startedAtUtc = DateTime.SpecifyKind(qr.CreatedAtUtc, DateTimeKind.Utc),
            serverNowUtc = DateTime.UtcNow, warningAfterSeconds = WarningSeconds, manualAfterSeconds = ManualSeconds,
            manual = qr.NotificationConfirmationSource == ManualReviewSource,
            ended = qr.Status != PosPaymentQrStatus.Pending || qr.PaymentId != null || order.Status != OrderStatus.Draft,
            phones = devices.Select(deviceMonitor.State).ToArray() };
    }
    private async Task<Order> RequireWaitingTerminalAsync(PosPaymentQrRequest qr, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(x => x.POSShift).SingleOrDefaultAsync(x => x.Id == qr.OrderId && x.StoreId == StoreId, ct);
        if (order == null || order.POSShift.TerminalId != runtime.TerminalId || order.POSShift.Status != POSShiftStatus.Open)
            throw new InvalidOperationException("QR không thuộc quầy/ca đang mở.");
        return order;
    }
    public async Task<object> BeginManualReviewAsync(int qrId, CancellationToken ct)
    {
        await using var gate = await locks.AcquireAsync(db, 0, ct);
        var paused = await PauseCoreAsync(qrId, false, ct);
        return new { qrId, manual = paused, ended = !paused };
    }
    private async Task<bool> PauseCoreAsync(int qrId, bool rejectedSuggestion, CancellationToken ct)
    {
        var hint = await db.PosPaymentQrRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == qrId && x.StoreId == StoreId, ct)
            ?? throw new InvalidOperationException("Không tìm thấy QR.");
        await using var orderGate = await locks.AcquireAsync(db, hint.OrderId, ct);
        var qr = await db.PosPaymentQrRequests.SingleAsync(x => x.Id == qrId && x.StoreId == StoreId, ct);
        await db.Entry(qr).ReloadAsync(ct);
        var order = await RequireWaitingTerminalAsync(qr, ct);
        if (qr.Status != PosPaymentQrStatus.Pending || qr.PaymentId != null || order.Status != OrderStatus.Draft) return false;
        if (qr.ConfirmMode != BankQrConfirmMode.Manual || qr.QrRenderMode != BankQrRenderMode.LocalEmvQr)
            throw new InvalidOperationException("QR ACB tự động cần được xử lý theo kết quả ngân hàng.");
        if (qr.NotificationConfirmationSource == ManualReviewSource) return true;
        if (!rejectedSuggestion && DateTime.UtcNow - qr.CreatedAtUtc < TimeSpan.FromSeconds(ManualSeconds))
            throw new InvalidOperationException($"Tiếp tục chờ tự động. Có thể kiểm tra thủ công sau {ManualSeconds} giây.");
        // Existing persisted QR audit field: no schema or menu changes are needed.
        qr.NotificationConfirmationSource = ManualReviewSource;
        await db.SaveChangesAsync(ct);
        return true;
    }
    private async Task<bool> HasManualAmbiguityAsync(BankNotificationReceipt receipt, CancellationToken ct)
    {
        var occurred = BankNotificationEvidence.OccurredAt(receipt);
        var original = BankNotificationParser.Parse(receipt.Content);
        var orderId = original?.OrderId ?? receipt.OrderId;
        var qrCode = original?.QrCode ?? receipt.QrCode;
        var paused = await (from q in db.PosPaymentQrRequests.AsNoTracking()
                            where q.StoreId == StoreId && q.BankAccountId == receipt.BankAccountId &&
                                q.Amount == receipt.Amount && q.CreatedAtUtc <= occurred.AddSeconds(5) &&
                                q.ExpireAtUtc >= occurred.AddSeconds(-5) &&
                                q.ConfirmMode == BankQrConfirmMode.Manual && q.QrRenderMode == BankQrRenderMode.LocalEmvQr &&
                                (q.NotificationConfirmationSource == ManualReviewSource || q.NotificationConfirmationSource == ManualCompletedSource ||
                                 q.NotificationConfirmationSource == AdminApprovalSource || q.NotificationConfirmationSource == AdminCompletedSource ||
                                 q.NotificationConfirmationSource == AdminReceiptApprovalSource || q.NotificationConfirmationSource == AdminReceiptCompletedSource ||
                                 q.Status == PosPaymentQrStatus.Cancelled ||
                                 q.Status == PosPaymentQrStatus.ManualConfirmed && q.NotificationReceiptId == null)
                            select new { q.OrderId, q.RequestCode }).ToListAsync(ct);
        // Without a request code, an old manual collection and a new QR for the
        // same order/amount cannot be distinguished. Retain the evidence for review.
        return paused.Any(x => (orderId == null || orderId == x.OrderId) &&
            (qrCode == null || string.Equals(qrCode, x.RequestCode, StringComparison.OrdinalIgnoreCase)));
    }
    public async Task RecordContactAsync(BankNotificationDevice device, CancellationToken ct)
    {
        if (device.StoreId != StoreId || !device.Enabled) throw new InvalidOperationException("Thiết bị không hợp lệ.");
        device.LastSeenAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        deviceMonitor.Contact(StoreId, device.Id);
    }
    public async Task HeartbeatAsync(BankNotificationDevice device, BankNotificationHeartbeat value, CancellationToken ct)
    {
        if (device.StoreId != StoreId || !device.Enabled || value.QueuedCount is < 0 or > 1000 ||
            value.BlockedCount < 0 || value.BlockedCount > value.QueuedCount || value.LastErrorCode is < 0 or > 599)
            throw new InvalidOperationException("Trạng thái điện thoại không hợp lệ.");
        device.LastSeenAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        deviceMonitor.Report(StoreId, device.Id, value);
    }
}
