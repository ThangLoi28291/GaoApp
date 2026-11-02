using GaoApp.Application.DTOs.Invoices;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Invoices;

public interface IInvoiceIntegrationLogRepository
{
    // Read methods return untracked logs with legacy bodies, URLs and errors
    // sanitized for display. Raw evidence remains stored; never expose it via DTOs.
    Task AddAsync(
        InvoiceIntegrationLog log,
        CancellationToken ct = default);

    Task<List<InvoiceIntegrationLog>> GetLatestByInvoiceHeadAsync(
        int invoiceHeadId,
        int take = 20,
        CancellationToken ct = default);

    Task<(List<InvoiceIntegrationLog> Items, int Total)> QueryAsync(
        InvoiceIntegrationLogQueryDto query,
        CancellationToken ct = default);

    Task<InvoiceIntegrationLog?> GetByIdAsync(
        int id,
        CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
    Task<List<InvoiceIntegrationLog>> GetLogsForDashboardAsync(
    DateTime fromDate,
    DateTime toDate,
    CancellationToken ct = default);
    Task<InvoiceIntegrationLogCleanupResultDto> CleanupAsync(
    InvoiceIntegrationLogCleanupRequestDto request,
    DateTime nowUtc,
    CancellationToken ct = default);
}
