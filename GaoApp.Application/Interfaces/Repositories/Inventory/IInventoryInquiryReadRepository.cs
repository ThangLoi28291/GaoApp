using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Application.Interfaces.Repositories.Inventory;

public interface IInventoryInquiryReadRepository
{
    Task<InventoryInquiryPageDto> QueryAsync(
        int storeId,
        InventoryInquiryQueryRequest request,
        int? costViewerUserId,
        CancellationToken ct = default);

    Task<InventoryInquiryQuickViewDto?> GetQuickViewAsync(
        int storeId,
        int warehouseId,
        int productVariantId,
        int? costViewerUserId,
        CancellationToken ct = default);
}
