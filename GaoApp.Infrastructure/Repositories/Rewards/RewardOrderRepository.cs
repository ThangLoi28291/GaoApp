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
        return await _db.Orders
            .AsNoTracking()
            .Include(x => x.Lines)
                .ThenInclude(x => x.Variant)
                    .ThenInclude(x => x!.Product)
                        .ThenInclude(x => x.Category)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);
    }
}