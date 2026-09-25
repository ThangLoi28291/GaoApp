using GaoApp.Application.Interfaces.Repositories.Rewards;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Rewards;

public sealed class RewardOrderRepository : IRewardOrderRepository
{
    private readonly AppDbContext _db;

    public RewardOrderRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Order?> GetOrderForRewardCalculationAsync(
        int orderId,
        CancellationToken ct = default)
    {
        var order = await _db.Orders
            .AsNoTracking()
            .Include(x => x.Lines)
                .ThenInclude(x => x.Variant)
                    .ThenInclude(x => x!.Product)
                        .ThenInclude(x => x.Category)
            .Include(x => x.Lines)
                .ThenInclude(x => x.Variant)
                    .ThenInclude(x => x!.UnitConversions.Where(unit => !unit.IsDeleted && unit.IsActive))
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

        if (order is null) return null;
        var categories = await _db.Categories.AsNoTracking()
            .Where(x => x.StoreId == order.StoreId && !x.IsDeleted).ToDictionaryAsync(x => x.Id, ct);
        foreach (var category in categories.Values)
            category.Parent = category.ParentId.HasValue ? categories.GetValueOrDefault(category.ParentId.Value) : null;
        foreach (var line in order.Lines)
            if (line.Variant?.Product is { } product)
                product.Category = categories.GetValueOrDefault(product.CategoryId)!;
        return order;
    }
}
