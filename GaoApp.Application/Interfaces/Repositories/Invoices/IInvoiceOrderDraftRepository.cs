using GaoApp.Application.DTOs.Invoices;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Invoices;

public interface IInvoiceOrderDraftRepository
{
    Task<List<InvoiceOrderDraftOrderDto>> SearchAsync(int storeId, string term, CancellationToken ct);
    Task<Order?> GetOrderAsync(int storeId, int orderId, bool lockForCreate, CancellationToken ct);
    Task<List<InvoiceOrderDraftReferenceDto>> GetExistingAsync(int storeId, int orderId, CancellationToken ct);
    Task<InvoiceProviderSetting?> GetProviderMetadataAsync(int storeId, int? settingId, CancellationToken ct);
}
