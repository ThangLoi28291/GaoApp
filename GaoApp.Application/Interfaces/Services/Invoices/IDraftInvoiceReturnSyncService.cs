namespace GaoApp.Application.Interfaces.Services.Invoices;

/// <summary>
/// Giữ hóa đơn bán ra chưa phát hành đồng bộ với các phiếu trả hàng POS.
/// Hóa đơn đã phát hành thuộc luồng xử lý riêng của kế toán.
/// </summary>
public interface IDraftInvoiceReturnSyncService
{
    /// <summary>
    /// Khóa đồng thời với luồng phát hành và xác nhận các dòng trả chưa thuộc
    /// hóa đơn đã phát hành/đang chờ xác minh kết quả phát hành.
    /// </summary>
    Task EnsurePosReturnAllowedAsync(
        int storeId,
        int orderId,
        IReadOnlyCollection<int> orderLineIds,
        CancellationToken ct = default);

    /// <summary>
    /// Giảm InvoiceDetail nháp theo reversal allocation thực tế của phiếu trả.
    /// </summary>
    Task SyncAfterReturnAsync(
        int orderId,
        int salesReturnId,
        CancellationToken ct = default);
}
