using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Application.Interfaces.Services.Invoices;

public interface IViettelInvoiceEmailService
{
    Task<Result<ViettelInvoiceSendEmailResultDto>> SendEmailAsync(
        ViettelInvoiceSendEmailRequestDto request,
        CancellationToken ct = default);
}