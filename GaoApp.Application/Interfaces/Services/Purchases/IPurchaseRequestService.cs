using GaoApp.Application.DTOs.Purchases;

namespace GaoApp.Application.Interfaces.Services.Purchases;

public interface IPurchaseRequestService
{
    Task<List<PurchaseRequestListItemDto>> GetListAsync(bool onlyMine, CancellationToken ct = default);
    Task<PurchaseRequestDetailDto?> GetDetailAsync(int id, bool requireOwnership, CancellationToken ct = default);
    Task<PurchaseRequestDetailDto?> GetDetailAsync(
        int id, bool requireOwnership, bool includeCost, CancellationToken ct = default);
    Task<int> SaveAsync(SavePurchaseRequestRequest request, bool submitAfterSave = false, CancellationToken ct = default);
    Task SubmitAsync(int id, PurchaseRequestWorkflowRequest request, CancellationToken ct = default);
    Task ApproveAsync(int id, ApprovePurchaseRequestRequest request, CancellationToken ct = default);
    Task ReturnForRevisionAsync(int id, PurchaseRequestWorkflowRequest request, CancellationToken ct = default);
    Task RejectAsync(int id, PurchaseRequestWorkflowRequest request, CancellationToken ct = default);
    Task CancelAsync(int id, PurchaseRequestWorkflowRequest request, bool requireOwnership, CancellationToken ct = default);
    Task<PurchaseRequestPreparationDto> GetPreparationAsync(int id, bool includeCost, CancellationToken ct = default);
    Task<ConvertPurchaseRequestResultDto> ConvertAsync(int id, ConvertPurchaseRequestRequest request, CancellationToken ct = default);
    Task<PurchaseLookupPageDto<PurchaseRequestProductLookupDto>> SearchProductsAsync(
        string? term, int page, CancellationToken ct = default);
    Task<List<PurchaseRequestProductLookupDto>> GetSelectedProductsAsync(
        IReadOnlyCollection<int> productUnitConversionIds, CancellationToken ct = default);
}
