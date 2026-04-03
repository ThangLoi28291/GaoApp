using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Application.Interfaces.Services.Inventory;

public interface IInventoryAdjustmentService
{
    Task<StockAdjustmentResultDto> CreateAsync(CreateStockAdjustmentRequest request, CancellationToken ct = default);
}