using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Application.Interfaces.Services.Invoices;

public interface IInvoiceIntegrationLogCleanupService
{
    Task<Result<InvoiceIntegrationLogCleanupResultDto>> CleanupAsync(
        InvoiceIntegrationLogCleanupRequestDto request,
        CancellationToken ct = default);
}