using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Application.Interfaces.Services.Inventory;

public interface IInventoryInquiryReadService
{
    Task<InventoryInquiryPageDto> GetPageAsync(
        InventoryInquiryQueryRequest request,
        CancellationToken ct = default);

    Task<InventoryInquiryQuickViewDto?> GetQuickViewAsync(
        int warehouseId,
        int productVariantId,
        CancellationToken ct = default);
}
