using GaoApp.Application.DTOs.Rewards;

namespace GaoApp.Application.Interfaces.Services.Rewards;

public interface IOrderRewardCalculator
{
    Task<OrderRewardCalculationDto> CalculateForReturnAsync(int orderId, CancellationToken ct = default);
    Task<OrderRewardCalculationDto> CalculateAsync(
        int orderId,
        CancellationToken ct = default);
}
