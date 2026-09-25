using GaoApp.Application.DTOs.Purchases;

namespace GaoApp.Application.Interfaces.Services.Purchases;

public interface IPurchaseRequestIndexReadService
{
    Task<PurchaseRequestIndexPageDto> GetPageAsync(
        PurchaseRequestIndexQueryRequest request,
        int? requestedByUserId,
        CancellationToken ct = default);

    Task<List<PurchaseRequestIndexRequesterOptionDto>> GetRequesterOptionsAsync(
        CancellationToken ct = default);

    Task<PurchaseRequestIndexQuickViewDto?> GetQuickViewAsync(
        int requestId,
        int? requestedByUserId,
        CancellationToken ct = default);
}
