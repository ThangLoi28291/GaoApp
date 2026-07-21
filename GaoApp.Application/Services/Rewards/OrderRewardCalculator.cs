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

    public async Task<OrderRewardCalculationDto> CalculateAsync(
        int orderId,
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

        foreach (var line in order.Lines)
        {
            var lineResult = CalculateLine(line);
            result.Lines.Add(lineResult);

            if (lineResult.IsRewardable)
            {
                result.RewardableAmount += lineResult.LineTotal;
            }
        }

        return result;
    }

    private static OrderRewardCalculationLineDto CalculateLine(GaoApp.Domain.Entities.OrderLine line)
    {
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

        // Product override ưu tiên hơn Category.
        var productRewardEligible = product.IsRewardEligibleOverride
            ?? category.IsRewardEligible;

        if (!productRewardEligible)
        {
            return MapLine(line, false, "Sản phẩm/ngành hàng không tích điểm.");
        }

        // Bán lốc/thùng hoặc conversion khác base unit thì không tích.
        if (line.Multiplier != 1m)
        {
            return MapLine(line, false, "Đơn vị bán không phải đơn vị gốc.");
        }

        // Nếu bán đơn vị gốc nhưng số lượng đạt ngưỡng lốc/thùng thì không tích.
        if (product.RewardBulkExcludeQuantity.HasValue &&
            product.RewardBulkExcludeQuantity.Value > 0 &&
            line.Quantity >= product.RewardBulkExcludeQuantity.Value)
        {
            return MapLine(line, false, "Số lượng đạt ngưỡng lốc/thùng, không tích điểm.");
        }

        if (line.LineTotal <= 0)
        {
            return MapLine(line, false, "Thành tiền không hợp lệ.");
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