using GaoApp.Domain.Entities;

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

    Task<InvoiceHead?> GetInvoiceHeadByIdAsync(
        int invoiceHeadId,
        CancellationToken ct = default);

    Task AddInvoiceHeadAsync(
        InvoiceHead invoiceHead,
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
}