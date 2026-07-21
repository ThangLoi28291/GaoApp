using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Invoices;

public interface IInvoiceCorrectionRepository
{
    Task<InvoiceHead?> GetOriginalInvoiceWithDetailsAsync(
        int invoiceHeadId,
        CancellationToken ct = default);

    Task<InvoiceCorrectionCase?> GetOpenCaseByOriginalAsync(
        int originalInvoiceHeadId,
        CancellationToken ct = default);

    Task AddInvoiceHeadAsync(
        InvoiceHead invoiceHead,
        CancellationToken ct = default);

    Task AddCorrectionCaseAsync(
        InvoiceCorrectionCase correctionCase,
        CancellationToken ct = default);
    Task<InvoiceCorrectionCase?> GetByNewInvoiceHeadIdAsync(
    int newInvoiceHeadId,
    CancellationToken ct = default);
    Task<InvoiceHead?> GetInvoiceHeadForHistoryAsync(
    int invoiceHeadId,
    CancellationToken ct = default);

    Task<List<InvoiceCorrectionCase>> GetCasesByOriginalInvoiceHeadIdAsync(
        int originalInvoiceHeadId,
        CancellationToken ct = default);
    Task<InvoiceCorrectionCase?> GetUnfinishedCaseByOriginalAsync(
    int originalInvoiceHeadId,
    CancellationToken ct = default);

    Task<InvoiceCorrectionCase?> GetActiveReplacementCaseByOriginalAsync(
        int originalInvoiceHeadId,
        CancellationToken ct = default);

    Task<List<InvoiceCorrectionCase>> GetActiveCasesByOriginalAsync(
        int originalInvoiceHeadId,
        CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}