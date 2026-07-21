using GaoApp.Application.DTOs.Purchases;

namespace GaoApp.Application.Interfaces.Services.Purchases;

public interface IPurchaseOrderService
{
    Task<PurchaseOrderListResultDto> GetListAsync(
        PurchaseOrderListQueryDto query,
        bool includeCost,
        bool canApprove,
        CancellationToken ct = default);
    Task<PurchaseOrderDetailDto?> GetDetailAsync(
        int id,
        bool includeCost,
        CancellationToken ct = default);
    Task<PurchaseOrderFormOptionsDto> GetFormOptionsAsync(CancellationToken ct = default);
    Task<PurchaseOrderFormOptionsDto> GetFormOptionsAsync(
        int? selectedSupplierId,
        IReadOnlyCollection<int>? selectedProductUnitConversionIds,
        CancellationToken ct = default);
    Task<PurchaseLookupPageDto<PurchaseSupplierLookupDto>> SearchSuppliersAsync(
        string? term, int page, CancellationToken ct = default);
    Task<PurchaseLookupPageDto<PurchaseProductLookupDto>> SearchProductsAsync(
        string? term, int? preferredSupplierId, int page, CancellationToken ct = default);
    Task<int> SaveAsync(SavePurchaseOrderRequest request, CancellationToken ct = default);
    Task UpdateSourceCommercialAsync(
        int id,
        UpdateSourcePurchaseOrderCommercialRequest request,
        CancellationToken ct = default);
    Task SubmitAsync(int id, PurchaseWorkflowRequest request, CancellationToken ct = default);
    Task ApproveAsync(int id, PurchaseWorkflowRequest request, CancellationToken ct = default);
    Task ReturnForRevisionAsync(int id, PurchaseWorkflowRequest request, CancellationToken ct = default);
    Task RejectAsync(int id, PurchaseWorkflowRequest request, CancellationToken ct = default);
    Task MarkSentAsync(int id, PurchaseWorkflowRequest request, CancellationToken ct = default);
    Task CancelAsync(int id, PurchaseWorkflowRequest request, CancellationToken ct = default);
}
