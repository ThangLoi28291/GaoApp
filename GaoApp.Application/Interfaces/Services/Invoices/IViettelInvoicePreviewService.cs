using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Application.Interfaces.Services.Invoices;

public interface IViettelInvoicePreviewService
{
    Task<Result<ViettelInvoicePreviewFileDto>> PreviewDraftPdfAsync(
        int invoiceHeadId,
        CancellationToken ct = default);
}