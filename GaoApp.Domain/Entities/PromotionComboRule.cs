using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

public class PromotionComboRule : BaseStoreEntity
{
    public int PromotionId { get; set; }
    public Promotion Promotion { get; set; } = default!;

    public int ProductId { get; set; }

    public int? VariantId { get; set; }

    public int? ProductUnitConversionId { get; set; }

    /// <summary>
    /// Số lượng bắt buộc trong combo.
    /// Ví dụ combo cần 1 lon Pepsi + 1 bánh + 1 sữa.
    /// </summary>
    public decimal RequiredQuantity { get; set; } = 1m;
}
