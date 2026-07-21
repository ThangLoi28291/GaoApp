using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

public class Promotion : BaseStoreEntity
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public PromotionType Type { get; set; }

    public PromotionDiscountType DiscountType { get; set; }

    public decimal DiscountValue { get; set; }

    public DateTime StartAtUtc { get; set; }

    public DateTime EndAtUtc { get; set; }

    public bool IsActive { get; set; }
    /// <summary>
    /// Độ ưu tiên khi nhiều chương trình cùng khớp.
    /// Số lớn hơn được ưu tiên trước.
    /// </summary>
    public int Priority { get; set; } = 0;
    /// <summary>
    /// Nhóm khách được áp dụng khuyến mãi.
    /// Null/rỗng = áp dụng tất cả.
    /// RETAIL = khách lẻ.
    /// WHOLESALE = khách sỉ.
    /// </summary>
    public string? CustomerPriceTier { get; set; }
    /// <summary>
    /// Giá cố định của combo.
    /// Chỉ dùng khi Type = ComboFixedPrice.
    /// Ví dụ: mua đủ 3 sản phẩm thì combo còn 30.000.
    /// </summary>
    public decimal? ComboFixedPrice { get; set; }

    /// <summary>
    /// Ghi chú hiển thị trên POS / bill.
    /// Ví dụ: Combo 3 sản phẩm giảm.
    /// </summary>
    public string? ComboNote { get; set; }
    /// <summary>
    /// Số lượng cần mua để được tặng.
    /// Ví dụ mua 10 tặng 1 => BuyQuantity = 10.
    /// </summary>
    public decimal? BuyQuantity { get; set; }

    /// <summary>
    /// Số lượng được tặng.
    /// Ví dụ mua 10 tặng 1 => GetQuantity = 1.
    /// </summary>
    public decimal? GetQuantity { get; set; }

    /// <summary>
    /// Có yêu cầu lấy đủ số lượng mua + tặng mới áp dụng không.
    /// True: khách phải lấy 11 mới được tính tiền 10.
    /// False: mua 10 thì tự hiểu tặng 1, thường dùng khi xuất thêm dòng quà tặng sau.
    /// Phase này nên dùng True.
    /// </summary>
    public bool RequireGiftQuantityInCart { get; set; } = true;

    public ICollection<PromotionComboRule> ComboRules { get; set; }
        = new List<PromotionComboRule>();

    public ICollection<PromotionItem> Items { get; set; }
        = new List<PromotionItem>();
}