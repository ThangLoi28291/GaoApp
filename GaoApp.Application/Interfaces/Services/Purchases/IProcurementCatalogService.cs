using GaoApp.Application.DTOs.Purchases;

namespace GaoApp.Application.Interfaces.Services.Purchases;

public interface IProcurementCatalogService
{
    Task<ProcurementQuickCreateOptionsDto> GetQuickCreateOptionsAsync(CancellationToken ct = default);

    Task<PurchaseLookupPageDto<PurchaseSupplierLookupDto>> SearchSuppliersAsync(
        string? term,
        int page,
        CancellationToken ct = default);

    Task ResolvePurchaseOrderLineAsync(
        int purchaseOrderId,
        int purchaseOrderLineId,
        ResolvePurchaseOrderLineRequest request,
        CancellationToken ct = default);

    Task<ProcurementCreatedProductDto> QuickCreateProductAsync(
        QuickCreateProcurementProductRequest request,
        bool canCreateUnit,
        CancellationToken ct = default);

    Task<ProcurementCreatedProductDto> QuickCreateAndResolvePurchaseOrderLineAsync(
        int purchaseOrderId,
        int purchaseOrderLineId,
        QuickCreateAndResolvePurchaseOrderLineRequest request,
        bool canCreateUnit,
        CancellationToken ct = default);
}
