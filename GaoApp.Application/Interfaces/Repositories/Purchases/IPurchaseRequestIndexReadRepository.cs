using GaoApp.Application.DTOs.Purchases;

namespace GaoApp.Application.Interfaces.Repositories.Purchases;

public interface IPurchaseRequestIndexReadRepository
{
    Task<PurchaseRequestIndexPageDto> QueryAsync(
        int storeId,
        int? requestedByUserId,
        PurchaseRequestIndexQueryRequest request,
        CancellationToken ct = default);

    Task<List<PurchaseRequestIndexRequesterOptionDto>> GetRequesterOptionsAsync(
        int storeId,
        CancellationToken ct = default);

    Task<PurchaseRequestIndexQuickViewDto?> GetQuickViewAsync(
        int storeId,
        int requestId,
        int? requestedByUserId,
        CancellationToken ct = default);
}
