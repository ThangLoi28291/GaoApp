namespace GaoApp.Application.DTOs.Rewards;

/// <summary>
/// Kết quả tính số tiền được tích điểm của một đơn hàng.
/// </summary>
public sealed class OrderRewardCalculationDto
{
    public int OrderId { get; set; }

    /// <summary>
    /// Tổng tiền đủ điều kiện tích điểm.
    /// </summary>
    public decimal RewardableAmount { get; set; }

    public List<OrderRewardCalculationLineDto> Lines { get; set; } = new();
}

public sealed class OrderRewardCalculationLineDto
{
    public int OrderLineId { get; set; }

    public int ProductId { get; set; }

    public int VariantId { get; set; }

    public string ItemName { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public decimal Multiplier { get; set; }

    public decimal LineTotal { get; set; }

    public bool IsRewardable { get; set; }

    public decimal RewardableAmount { get; set; }

    public string Reason { get; set; } = string.Empty;
}
