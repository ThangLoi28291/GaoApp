using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Web.Services.Acb;

public sealed record AcbConfirmationInfo(string Code, string Label, DateTime? ConfirmedAtUtc,
    int? UserId, string? UserName, int? CallbackReceiptId, bool Recorded);

public static class AcbConfirmationAudit
{
    public static AcbConfirmationInfo ForSession(AcbQrSession session, IReadOnlyDictionary<int, string> users, bool recorded)
    {
        var source = session.ConfirmationSource;
        var label = source switch
        {
            AcbConfirmationSource.ScheduledCheck => "Tự kiểm tra theo thời gian",
            AcbConfirmationSource.ManualCheck => "Thu ngân bấm Kiểm tra ngay",
            AcbConfirmationSource.Callback => "Callback ACB · Tức thời",
            AcbConfirmationSource.DailyCallback => "Callback ACB · Danh sách cuối ngày",
            AcbConfirmationSource.InvoiceLookup => "Tra cứu ACB tại hóa đơn",
            AcbConfirmationSource.CancellationCheck => "Kiểm tra ACB khi yêu cầu hủy QR",
            AcbConfirmationSource.QrRecoveryCheck => "Kiểm tra lại QR đã lưu",
            AcbConfirmationSource.OfflineManual => "Thu ngân xác nhận thủ công khi mất kết nối",
            _ => recorded || session.Status is AcbSessionStatus.Received or AcbSessionStatus.Completed
                ? "Chưa lưu nguồn xác nhận" : "Chưa xác nhận hợp lệ"
        };
        return new(source?.ToString() ?? (recorded || session.Status is AcbSessionStatus.Received or AcbSessionStatus.Completed ? "Unknown" : "Pending"),
            label, session.ConfirmedAtUtc, session.ConfirmedByUserId, UserName(session.ConfirmedByUserId, users),
            session.ConfirmationCallbackReceiptId, recorded);
    }

    public static AcbConfirmationInfo ForManual(PosPaymentQrRequest? qr, OrderPayment? payment, IReadOnlyDictionary<int, string> users)
    {
        var confirmed = payment != null || qr?.ManualConfirmedAtUtc != null || qr?.Status == PosPaymentQrStatus.ManualConfirmed;
        var userId = qr?.ManualConfirmedByUserId ?? payment?.CreatedBy;
        return new(confirmed ? qr == null ? "ManualEntry" : "ManualQr" : "Pending",
            confirmed ? qr == null ? "Thu ngân ghi nhận chuyển khoản" : "Thu ngân xác nhận QR thủ công" : "Chưa xác nhận nhận tiền",
            qr?.ManualConfirmedAtUtc ?? payment?.PaidAtUtc, userId, UserName(userId, users), null, payment != null);
    }

    private static string? UserName(int? id, IReadOnlyDictionary<int, string> users) =>
        id.HasValue ? users.GetValueOrDefault(id.Value) ?? $"Người dùng #{id}" : null;
}
