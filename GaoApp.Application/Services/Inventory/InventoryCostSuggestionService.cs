using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;

namespace GaoApp.Application.Services.Inventory;

public class InventoryCostSuggestionService : IInventoryCostSuggestionService
{
    private readonly IInventoryCostSuggestionRepository _repository;

    public InventoryCostSuggestionService(
        IInventoryCostSuggestionRepository repository)
    {
        _repository = repository;
    }

    public Task<InventoryCostSuggestionDto> GetSuggestedCostAsync(
        int productVariantId,
        CancellationToken ct = default)
    {
        return _repository.GetSuggestedCostAsync(productVariantId, ct);
    }
}