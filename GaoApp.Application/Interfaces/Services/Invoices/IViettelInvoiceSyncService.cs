using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Application.Interfaces.Services.Invoices;

public interface IViettelInvoiceSyncService
{
    Task<Result<ViettelInvoiceLookupResultDto>> SyncByTransactionUuidAsync(
        int invoiceHeadId,
        CancellationToken ct = default);
}