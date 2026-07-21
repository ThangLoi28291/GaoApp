using GaoApp.Application.DTOs.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Interfaces.Repositories.Invoices;

public interface IInvoiceRepository
{
    Task<Order?> GetOrderForInvoiceAsync(
        int orderId,
        CancellationToken ct = default);

    Task<Order?> GetOrderWithLinesForInvoiceAsync(
        int orderId,
        CancellationToken ct = default);

    Task<InvoiceHead?> GetInvoiceHeadByOrderIdAsync(
        int orderId,
        CancellationToken ct = default);

    Task<InvoiceHead?> GetInvoiceHeadWithDetailsByOrderIdAsync(
        int orderId,
        CancellationToken ct = default);

    Task<List<InvoiceHead>> GetOriginalInvoiceHeadsWithDetailsByOrderIdAsync(
        int orderId,
        CancellationToken ct = default);

    Task<InvoiceHead?> GetInvoiceHeadByIdAsync(
        int invoiceHeadId,
        CancellationToken ct = default);

    Task AddInvoiceHeadAsync(
        InvoiceHead invoiceHead,
        CancellationToken ct = default);

    Task AddInvoiceHeadsAsync(
        IReadOnlyCollection<InvoiceHead> invoiceHeads,
        CancellationToken ct = default);

    Task AddInvoiceDetailsAsync(
        List<InvoiceDetail> details,
        CancellationToken ct = default);
    Task<InvoiceHead?> GetInvoiceHeadWithDetailsByIdAsync(
    int invoiceHeadId,
    CancellationToken ct = default);

    Task AddInvoiceDetailAsync(
        InvoiceDetail detail,
        CancellationToken ct = default);
    Task SaveChangesAsync(
        CancellationToken ct = default);
    Task<(List<InvoiceHead> Items, int Total)> QueryInvoiceHeadsAsync(
        DateTime? fromDate,
        DateTime? toDate,
        int? orderId,
        string? keyword,
        InvoiceListDisplayMode displayMode,

        // GHI CHÚ:
        // Thêm SortMode để người dùng chọn kiểu sắp xếp trên màn hình.
        InvoiceListSortMode sortMode,

        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<InvoiceHead?> GetInvoiceHeadDetailAsync(
        int invoiceHeadId,
        CancellationToken ct = default);
    Task<InvoiceDetail?> GetInvoiceDetailByIdAsync(
    int invoiceDetailId,
    CancellationToken ct = default);

    void UpdateInvoiceHead(InvoiceHead invoiceHead);
    Task<List<InvoiceHead>> GetByTransactionUuidsAsync(
       List<string> transactionUuids,
       CancellationToken ct = default);

    Task<List<InvoiceHead>> GetByProviderInvoiceNosAsync(
        List<string> providerInvoiceNos,
        CancellationToken ct = default);

    Task<List<InvoiceHead>> GetInvoiceHeadsForDashboardAsync(
        DateTime fromDate,
        DateTime toDate,
        CancellationToken ct = default);
    Task<InvoiceHead?> GetLatestBuyerInfoByTaxCodeAsync(
     int storeId,
     string taxCode,
     CancellationToken ct = default);
    Task<(ViettelInvoiceDashboardSummaryDto Summary, List<InvoiceHead> Items, int Total)> QueryViettelDashboardAsync(
    ViettelInvoiceDashboardQueryDto query,
    CancellationToken ct = default);

}
