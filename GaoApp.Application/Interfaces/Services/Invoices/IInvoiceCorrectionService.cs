using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Interfaces.Services.Invoices;

public interface IInvoiceCorrectionService
{
    Task<Result<InvoiceCorrectionCreateInfoDto>> GetCreateInfoAsync(
        int originalInvoiceHeadId,
        InvoiceCorrectionType type,
        CancellationToken ct = default);

    Task<Result<CreateInvoiceCorrectionResultDto>> CreateAsync(
        CreateInvoiceCorrectionRequestDto request,
        CancellationToken ct = default);
    Task<Result<InvoiceCorrectionHistoryDto>> GetHistoryAsync(
    int invoiceHeadId,
    CancellationToken ct = default);
}