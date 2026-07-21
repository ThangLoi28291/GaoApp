using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Application.Interfaces.Services.Invoices;

public interface IInvoiceIntegrationLogService
{
    Task<Result<PagedResult<InvoiceIntegrationLogListItemDto>>> GetLogsAsync(
        InvoiceIntegrationLogQueryDto query,
        CancellationToken ct = default);

    Task<Result<InvoiceIntegrationLogDetailDto>> GetDetailAsync(
        int id,
        CancellationToken ct = default);
}