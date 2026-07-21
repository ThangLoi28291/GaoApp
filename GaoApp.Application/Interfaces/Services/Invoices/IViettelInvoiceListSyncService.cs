using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Application.Interfaces.Services.Invoices;

public interface IViettelInvoiceListSyncService
{
    Task<Result<ViettelInvoiceListSyncResultDto>> SyncAsync(
        ViettelInvoiceListSyncRequestDto request,
        CancellationToken ct = default);
}