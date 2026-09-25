using GaoApp.Application.DTOs.Rewards;
using GaoApp.Application.Interfaces.Repositories.Rewards;
using GaoApp.Application.Interfaces.Services.Rewards;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Rewards;

/// <summary>
/// Tính số tiền đủ điều kiện tích điểm của một đơn hàng.
/// Chỉ tính toán, không ghi ledger.
/// </summary>
public sealed class OrderRewardCalculator : IOrderRewardCalculator
{
    private readonly IRewardOrderRepository _orderRepository;

    public OrderRewardCalculator(IRewardOrderRepository orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public Task<OrderRewardCalculationDto> CalculateAsync(int orderId, CancellationToken ct = default)
        => CalculateCoreAsync(orderId, false, ct);

    public Task<OrderRewardCalculationDto> CalculateForReturnAsync(int orderId, CancellationToken ct = default)
        => CalculateCoreAsync(orderId, true, ct);

    private async Task<OrderRewardCalculationDto> CalculateCoreAsync(
        int orderId,
        bool legacyWhenNoSnapshot,
        CancellationToken ct = default)
    {
        if (orderId <= 0)
            throw new InvalidOperationException("Đơn hàng không hợp lệ.");

        var order = await _orderRepository.GetOrderForRewardCalculationAsync(orderId, ct)
            ?? throw new InvalidOperationException("Không tìm thấy đơn hàng.");

        var result = new OrderRewardCalculationDto
        {
            OrderId = order.Id
        };

        if (!order.CustomerId.HasValue || order.CustomerId.Value <= 0)
        {
            AddAllLinesAsNotRewardable(order, result, "Đơn không có khách hàng.");
            return result;
        }

        if (order.Status != OrderStatus.Completed)
        {
            AddAllLinesAsNotRewardable(order, result, "Đơn chưa hoàn tất.");
            return result;
        }

        foreach (var line in order.Lines.Where(line => !line.IsDeleted))
        {
            var lineResult = CalculateLine(line, legacyWhenNoSnapshot);
            result.Lines.Add(lineResult);

            if (lineResult.IsRewardable)
            {
                result.RewardableAmount += lineResult.RewardableAmount;
            }
        }

        return result;
    }

    private static OrderRewardCalculationLineDto CalculateLine(GaoApp.Domain.Entities.OrderLine line, bool legacyWhenNoSnapshot)
    {
        if (line.RewardableAmountSnapshot.HasValue)
            return MapLine(line, line.RewardableAmountSnapshot > 0, "Theo tích điểm đã ghi nhận khi thanh toán.");

        var product = line.Variant?.Product;
        var category = product?.Category;

        if (product == null)
        {
            return MapLine(line, false, "Không tìm thấy sản phẩm.");
        }

        if (category == null)
        {
            return MapLine(line, false, "Không tìm thấy ngành hàng.");
        }

        // Preserve the old rules only for returns of orders without an earning snapshot.
        // For new sales an excluded category (including ancestors) cannot be overridden.
        if (!legacyWhenNoSnapshot)
        {
            var visited = new HashSet<int>();
            for (var current = category; current != null; current = current.Parent)
            {
                if (!visited.Add(current.Id) || !current.IsRewardEligible || current.IsDeleted
                    || (current.ParentId.HasValue && current.Parent == null))
                    return MapLine(line, false, "Ngành hàng nằm trong danh sách không tích điểm.");
            }
        }

        // Product override can disable earning, but cannot enable an excluded category.
        var productRewardEligible = product.IsRewardEligibleOverride
            ?? category.IsRewardEligible;

        if (!productRewardEligible)
        {
            return MapLine(line, false, "Sản phẩm/ngành hàng không tích điểm.");
        }

        // Bán lốc/thùng hoặc conversion khác base unit thì không tích.
        if (line.Multiplier != 1m || (!legacyWhenNoSnapshot
            && line.SellingUnitId != (line.BaseUnitId ?? product.BaseUnitId)))
        {
            return MapLine(line, false, "Đơn vị bán không phải đơn vị gốc.");
        }

        // Nếu bán đơn vị gốc nhưng số lượng đạt ngưỡng lốc/thùng thì không tích.
        if (legacyWhenNoSnapshot && product.RewardBulkExcludeQuantity.HasValue &&
            product.RewardBulkExcludeQuantity.Value > 0 &&
            line.Quantity >= product.RewardBulkExcludeQuantity.Value)
        {
            return MapLine(line, false, "Số lượng đạt ngưỡng lốc/thùng, không tích điểm.");
        }

        if (line.Quantity <= 0 || line.LineTotal <= 0 || line.IsPromotionGift)
        {
            return MapLine(line, false, "Thành tiền không hợp lệ.");
        }

        if (!legacyWhenNoSnapshot)
        {
            var baseConversion = line.Variant!.UnitConversions
                .Where(unit => !unit.IsDeleted && unit.IsActive && unit.Factor == 1
                    && unit.UnitId == (line.BaseUnitId ?? product.BaseUnitId))
                .OrderByDescending(unit => unit.IsBaseUnit).ThenBy(unit => unit.Id).FirstOrDefault();
            var retailPrice = line.RewardBaseUnitPrice
                ?? (baseConversion?.Price is > 0 ? baseConversion.Price.Value
                    : line.Variant.Price is > 0 ? line.Variant.Price.Value : product.BasePrice);
            if (retailPrice <= 0 || line.UnitPrice != retailPrice
                || line.LineDiscount > 0 || line.PromotionDiscount > 0 || line.ComboAllocatedDiscount > 0
                || line.LineTotal != Math.Round(line.Quantity * retailPrice, 0, MidpointRounding.AwayFromZero))
                return MapLine(line, false, "Giá thực bán khác giá lẻ đơn vị gốc hoặc dòng hàng đã giảm giá.");
        }

        return MapLine(line, true, "Được tích điểm.");
    }

    private static OrderRewardCalculationLineDto MapLine(
        GaoApp.Domain.Entities.OrderLine line,
        bool isRewardable,
        string reason)
    {
        return new OrderRewardCalculationLineDto
        {
            OrderLineId = line.Id,
            ProductId = line.ProductId,
            VariantId = line.VariantId,
            ItemName = line.ItemName,
            Quantity = line.Quantity,
            Multiplier = line.Multiplier,
            LineTotal = line.LineTotal,
            IsRewardable = isRewardable,
            RewardableAmount = isRewardable ? line.RewardableAmountSnapshot ?? line.LineTotal : 0,
            Reason = reason
        };
    }

    private static void AddAllLinesAsNotRewardable(
        GaoApp.Domain.Entities.Order order,
        OrderRewardCalculationDto result,
        string reason)
    {
        foreach (var line in order.Lines)
        {
            result.Lines.Add(MapLine(line, false, reason));
        }
    }
}
