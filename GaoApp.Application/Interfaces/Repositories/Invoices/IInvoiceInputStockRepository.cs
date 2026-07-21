using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Application.Interfaces.Repositories.Invoices;

/// <summary>
/// Đọc sổ tồn hàng có chứng từ hóa đơn đầu vào phục vụ preflight phát hành HĐĐT.
/// </summary>
public interface IInvoiceInputStockRepository
{
    /// <summary>
    /// Khóa tuần tự thao tác phát hành trong một store đến hết transaction hiện tại.
    /// Ngăn hai request cùng tiêu một lượng tồn có hóa đơn.
    /// </summary>
    Task LockStoreForIssueAsync(int storeId, CancellationToken ct = default);

    Task<InvoiceInputStockAvailabilityDto> GetAvailabilityAsync(
        int invoiceHeadId,
        CancellationToken ct = default);
}
