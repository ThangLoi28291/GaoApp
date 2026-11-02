using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Application.Interfaces.Services.Invoices;

public interface IInvoiceOrderDraftService
{
    Task<Result<List<InvoiceOrderDraftOrderDto>>> SearchAsync(string? term, CancellationToken ct = default);
    Task<Result<InvoiceOrderDraftResultDto>> PreviewAsync(int orderId, CancellationToken ct = default);
    Task<Result<InvoiceOrderDraftResultDto>> CreateAsync(int orderId, CancellationToken ct = default);
}
