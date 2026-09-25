using GaoApp.Application.DTOs.Purchases;

namespace GaoApp.Application.Interfaces.Repositories.Purchases;

public interface IPurchaseOrderIndexReadRepository
{
    Task<PurchaseOrderIndexPageDto> QueryAsync(
        int storeId,
        PurchaseOrderIndexQueryRequest request,
        bool includeCost,
        CancellationToken ct = default);

    Task<PurchaseOrderIndexFilterOptionsDto> GetFilterOptionsAsync(
        int storeId,
        CancellationToken ct = default);

    Task<PurchaseOrderIndexQuickViewDto?> GetQuickViewAsync(
        int storeId,
        int orderId,
        bool includeCost,
        CancellationToken ct = default);
}
