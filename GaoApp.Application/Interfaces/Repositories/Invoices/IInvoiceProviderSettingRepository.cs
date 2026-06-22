using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Invoices;

public interface IInvoiceProviderSettingRepository
{
    Task<List<InvoiceProviderSetting>> GetAllAsync(CancellationToken ct = default);

    Task<InvoiceProviderSetting?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Lấy cấu hình Viettel active mới nhất.
    /// Bản cũ giữ lại để không làm lỗi code hiện tại.
    /// </summary>
    Task<InvoiceProviderSetting?> GetActiveViettelAsync(CancellationToken ct = default);

    /// <summary>
    /// Lấy cấu hình Viettel active theo cửa hàng.
    /// Dùng cho tạo InvoiceHead để tránh lấy nhầm cấu hình store khác.
    /// </summary>
    Task<InvoiceProviderSetting?> GetActiveViettelAsync(
        int storeId,
        CancellationToken ct = default);

    Task<bool> ExistsDuplicateAsync(
        int id,
        string providerCode,
        string supplierTaxCode,
        string templateCode,
        string invoiceSeries,
        CancellationToken ct = default);

    Task AddAsync(InvoiceProviderSetting entity, CancellationToken ct = default);

    void Update(InvoiceProviderSetting entity);

    Task SaveChangesAsync(CancellationToken ct = default);
}