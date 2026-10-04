using GaoApp.Application.DTOs.POSPaymentQrs;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.Acb;

public sealed partial class AcbPaymentService
{
    // These reads never call ACB, create a request, update status, or record a payment.
    public async Task<PosQrHistory> QrHistoryAsync(int orderId, CancellationToken ct)
    {
        var order = await OrderAsync(orderId, ct);
        var qrs = await db.PosPaymentQrRequests.AsNoTracking().Include(x => x.BankAccount)
            .Where(x => x.StoreId == StoreId && x.OrderId == orderId)
            .OrderByDescending(x => x.Id).ToListAsync(ct);
        var sessions = await Sessions.AsNoTracking().Where(x => x.OrderId == orderId).ToListAsync(ct);
        var items = qrs.Select(qr =>
        {
            var session = sessions.SingleOrDefault(x => x.QrRequestId == qr.Id);
            return new PosQrHistoryItem(qr.Id, qr.RequestCode, qr.Amount, qr.CreatedAtUtc,
                session?.Status == AcbSessionStatus.Received && session.PaymentId != null ? "Recorded" : session?.Status.ToString() ?? qr.Status.ToString(), qr.BankAccount.BankName,
                session != null, CanReopen(order, qr, session), session?.ReviewReason);
        }).ToList();
        return new(orderId, items.FirstOrDefault(x => x.CanReopen)?.QrId, items);
    }

    public async Task<PosSavedQr> ReopenQrAsync(int orderId, int qrId, CancellationToken ct)
    {
        var order = await OrderAsync(orderId, ct);
        var qr = await db.PosPaymentQrRequests.AsNoTracking().Include(x => x.BankAccount)
            .SingleOrDefaultAsync(x => x.StoreId == StoreId && x.OrderId == orderId && x.Id == qrId, ct)
            ?? throw new InvalidOperationException("Không tìm thấy QR của đơn hàng này.");
        var session = await Sessions.AsNoTracking().SingleOrDefaultAsync(x => x.QrRequestId == qrId && x.OrderId == orderId, ct);
        RequireTerminal(order, session);
        if (!CanReopen(order, qr, session))
            throw new InvalidOperationException("Lần tạo QR chưa có ảnh mã. Mở lịch sử giao dịch để tra cứu hoặc hủy lần tạo này trước khi thử lại.");
        var dto = new POSPaymentQrDto
        {
            Id = qr.Id, OrderId = qr.OrderId, BankAccountId = qr.BankAccountId,
            BankCode = qr.BankAccount.BankCode, BankName = qr.BankAccount.BankName,
            AccountNumber = session?.VirtualAccount ?? qr.BankAccount.AccountNumber,
            AccountName = qr.BankAccount.AccountName, Amount = qr.Amount, Content = qr.Content,
            RequestCode = qr.RequestCode, Status = qr.Status, QrDataUrl = qr.QrDataUrl ?? "",
            QrRawText = qr.QrRawText ?? "", ExpireAtUtc = qr.ExpireAtUtc, AutomaticConfirmation = session != null
        };
        var readOnly = order.Status != OrderStatus.Draft || (session == null
            ? qr.Status != PosPaymentQrStatus.Pending
            : session.Status is AcbSessionStatus.Cancelled or AcbSessionStatus.Completed ||
              (session.PaymentId != null && AcbPaymentPolicy.Balance(order) > 0));
        var canCancel = session == null
            ? order.Status == OrderStatus.Draft && qr.PaymentId == null &&
              qr.Status is PosPaymentQrStatus.Pending or PosPaymentQrStatus.Failed or PosPaymentQrStatus.Expired
            : !readOnly && session.Status == AcbSessionStatus.Pending;
        var message = session?.Status switch
        {
            AcbSessionStatus.Received => "ACB đã xác nhận tiền. Bấm Kiểm tra ngay để tiếp tục ghi nhận và chốt đơn; không chuyển thêm tiền.",
            AcbSessionStatus.ReviewRequired => "QR cần kiểm tra: " + session.ReviewReason + " Bấm Kiểm tra ngay để đối chiếu lại; không chuyển thêm tiền.",
            _ => "Đang xem lại QR đã tạo. Số tiền và mã chuyển khoản giữ nguyên."
        };
        if (readOnly) message = "QR đã kết thúc hoặc đã ghi nhận tiền. Chỉ xem lại thông tin, không chuyển thêm vào QR này.";
        if (readOnly && canCancel) message = "QR chưa ghi nhận tiền và đã hết hạn hoặc không còn hợp lệ. Hủy QR này trước khi tạo QR mới.";
        return new(dto, session?.Status.ToString() ?? qr.Status.ToString(), canCancel, message, readOnly);
    }

    private bool CanReopen(Order order, PosPaymentQrRequest qr, AcbQrSession? session) =>
        order.POSShift.Status == POSShiftStatus.Open &&
        runtime.StoreId == StoreId && runtime.TerminalId == order.POSShift.TerminalId &&
        !string.IsNullOrWhiteSpace(qr.QrDataUrl) &&
        (session == null || session.ShiftId == order.POSShiftId && session.TerminalId == runtime.TerminalId);
}

public sealed record PosQrHistory(int OrderId, int? LatestQrId, IReadOnlyList<PosQrHistoryItem> Items);
public sealed record PosQrHistoryItem(int QrId, string RequestCode, decimal Amount, DateTime CreatedAtUtc,
    string Status, string BankName, bool AutomaticConfirmation, bool CanReopen, string? ReviewReason);
public sealed record PosSavedQr(POSPaymentQrDto Qr, string Status, bool CanCancel, string Message, bool ReadOnly = false);
