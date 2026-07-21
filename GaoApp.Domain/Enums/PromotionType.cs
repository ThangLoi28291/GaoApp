namespace GaoApp.Domain.Enums;

public enum PromotionType
{
    /// <summary>
    /// Giảm trực tiếp trên sản phẩm.
    /// Ví dụ: giảm 10%.
    /// </summary>
    ProductDiscount = 1,

    /// <summary>
    /// Combo nhiều sản phẩm, mua đủ bộ thì tổng còn giá cố định.
    /// </summary>
    ComboFixedPrice = 2,

    /// <summary>
    /// Mua X tặng Y.
    /// Ví dụ: mua 10 tặng 1.
    /// </summary>
    BuyXGetY = 3
}