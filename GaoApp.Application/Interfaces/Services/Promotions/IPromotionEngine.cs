using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Services.Promotions;

public interface IPromotionEngine
{
    Task ApplyLinePromotionAsync(
     Order order,
     OrderLine line,
     CancellationToken ct = default);

    Task ApplyOrderComboPromotionAsync(
    Order order,
    CancellationToken ct = default);
    Task ClearLinePromotionAsync(OrderLine line);
    Task ApplyOrderPromotionsAsync(
    Order order,
    IReadOnlyList<Promotion> productPromotions,
    IReadOnlyList<Promotion> comboPromotions,
    CancellationToken ct = default);
}