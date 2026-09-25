using GaoApp.Application.DTOs.Purchases;

namespace GaoApp.Application.Interfaces.Services.Purchases;

public interface IPurchaseOrderIndexReadService
{
    Task<PurchaseOrderIndexPageDto> GetPageAsync(
        PurchaseOrderIndexQueryRequest request,
        bool includeCost,
        CancellationToken ct = default);

    Task<PurchaseOrderIndexFilterOptionsDto> GetFilterOptionsAsync(
        CancellationToken ct = default);

    Task<PurchaseOrderIndexQuickViewDto?> GetQuickViewAsync(
        int orderId,
        bool includeCost,
        CancellationToken ct = default);
}
