using GaoApp.Application.DTOs.Promotions;
using GaoApp.Application.Interfaces.Repositories.Promotions;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Promotions;

internal sealed class PromotionTestRepository : IPromotionRepository
{
    public List<Promotion> Promotions { get; } = [];
    public Dictionary<int, PromotionProductLookupDto> Products { get; } = [];
    public Task<List<Promotion>> GetActiveProductDiscountPromotionsAsync(int storeId, CancellationToken ct = default)
        => Task.FromResult(Promotions.Where(x => x.StoreId == storeId && x.Type != PromotionType.ComboFixedPrice).ToList());
    public Task<List<Promotion>> GetActiveComboPromotionsAsync(int storeId, CancellationToken ct = default)
        => Task.FromResult(Promotions.Where(x => x.StoreId == storeId && x.Type == PromotionType.ComboFixedPrice).ToList());
    public Task<(IReadOnlyList<Promotion> Items, int TotalItems)> GetPagedAsync(int storeId, PromotionType? type,
        bool? isActive, string? customerPriceTier, string? keyword, DateTime? fromUtc, DateTime? toUtc,
        int page, int pageSize, CancellationToken ct = default)
        => Task.FromResult(((IReadOnlyList<Promotion>)Promotions.Where(x => x.StoreId == storeId).ToList(), Promotions.Count));
    public Task<Promotion?> GetByIdAsync(int storeId, int id, CancellationToken ct = default)
        => Task.FromResult(Promotions.FirstOrDefault(x => x.StoreId == storeId && x.Id == id));
    public Task<Promotion?> GetByIdForUpdateAsync(int storeId, int id, CancellationToken ct = default)
        => GetByIdAsync(storeId, id, ct);
    public Task<bool> ExistsNameAsync(int storeId, string name, int? excludeId, CancellationToken ct = default)
        => Task.FromResult(Promotions.Any(x => x.StoreId == storeId && x.Name == name && x.Id != excludeId));
    public Task AddAsync(Promotion entity, CancellationToken ct = default)
    {
        entity.Id = Promotions.Count + 1;
        Promotions.Add(entity);
        return Task.CompletedTask;
    }
    public void Update(Promotion entity) { }
    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<List<PromotionProductLookupDto>> SearchProductsForPromotionAsync(int storeId,
        string keyword, int take = 20, CancellationToken ct = default)
        => Task.FromResult(Products.Values.ToList());
    public Task<List<PromotionProductUnitLookupDto>> GetUnitsForPromotionAsync(int storeId,
        int variantId, CancellationToken ct = default) => Task.FromResult(new List<PromotionProductUnitLookupDto>());
    public Task<PromotionProductLookupDto?> GetProductForPromotionAsync(int storeId,
        int variantId, CancellationToken ct = default) => Task.FromResult(Products.GetValueOrDefault(variantId));
}
