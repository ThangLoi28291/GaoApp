namespace GaoApp.Domain.Enums;

/// <summary>
/// Trạng thái tích hợp hóa đơn điện tử với nhà cung cấp bên ngoài.
/// Hiện dùng cho Viettel SInvoice.
/// </summary>
public enum InvoiceProviderStatus
{
    /// <summary>
    /// Hóa đơn mới tạo trong GaoApp, chưa gửi sang nhà cung cấp.
    /// </summary>
    LocalDraft = 0,

    /// <summary>
    /// Đã đủ dữ liệu và sẵn sàng gửi.
    /// </summary>
    ReadyToIssue = 1,

    /// <summary>
    /// Đã gọi API xem trước hóa đơn.
    /// </summary>
    Previewed = 2,

    /// <summary>
    /// Đã gửi hóa đơn nháp lên SInvoice.
    /// </summary>
    DraftSent = 3,

    /// <summary>
    /// Đang gọi API phát hành.
    /// </summary>
    Issuing = 4,

    /// <summary>
    /// Đã phát hành thành công và đã có số hóa đơn.
    /// </summary>
    Issued = 5,

    /// <summary>
    /// API báo thành công nhưng chưa trả số hóa đơn.
    /// Cần tra cứu lại bằng transactionUuid.
    /// </summary>
    IssuedWaitingNumber = 6,

    /// <summary>
    /// Gửi phát hành bị lỗi.
    /// </summary>
    IssueFailed = 7,

    /// <summary>
    /// Đã hủy/xóa bỏ trên hệ thống hóa đơn điện tử.
    /// </summary>
    Cancelled = 8,

    /// <summary>
    /// Đã tải file PDF.
    /// </summary>
    PdfDownloaded = 9,

    /// <summary>
    /// Đã tải file XML/ZIP.
    /// </summary>
    ZipDownloaded = 10,

    /// <summary>
    /// Đã gửi email cho người mua.
    /// </summary>
    EmailSent = 11
}