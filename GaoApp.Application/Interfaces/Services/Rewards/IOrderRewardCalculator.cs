using GaoApp.Application.DTOs.Rewards;

namespace GaoApp.Application.Interfaces.Services.Rewards;

public interface IOrderRewardCalculator
{
    Task<OrderRewardCalculationDto> CalculateAsync(
        int orderId,
        CancellationToken ct = default);
}