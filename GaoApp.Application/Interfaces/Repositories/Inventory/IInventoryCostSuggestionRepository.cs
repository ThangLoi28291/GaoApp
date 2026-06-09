using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Application.Interfaces.Repositories.Inventory;

public interface IInventoryCostSuggestionRepository
{
    Task<InventoryCostSuggestionDto> GetSuggestedCostAsync(
        int productVariantId,
        CancellationToken ct = default);
}