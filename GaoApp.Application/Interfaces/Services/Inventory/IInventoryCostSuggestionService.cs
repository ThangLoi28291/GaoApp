using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Application.Interfaces.Services.Inventory;

public interface IInventoryCostSuggestionService
{
    Task<InventoryCostSuggestionDto> GetSuggestedCostAsync(
        int productVariantId,
        CancellationToken ct = default);
}