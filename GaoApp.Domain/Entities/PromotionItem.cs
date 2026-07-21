using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

public class PromotionItem : BaseStoreEntity
{
    public int PromotionId { get; set; }

    public Promotion Promotion { get; set; } = default!;

    public int ProductId { get; set; }

    public int? VariantId { get; set; }

    public int? ProductUnitConversionId { get; set; }
    /// <summary>
    /// Số lượng tối thiểu để được áp dụng khuyến mãi.
    /// </summary>
    public decimal MinQuantity { get; set; } = 1m;
}