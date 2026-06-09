using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Rewards;

public interface IRewardOrderRepository
{
    Task<Order?> GetOrderForRewardCalculationAsync(
        int orderId,
        CancellationToken ct = default);
}