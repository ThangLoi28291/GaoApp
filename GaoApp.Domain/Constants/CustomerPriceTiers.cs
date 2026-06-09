namespace GaoApp.Domain.Constants;

/// <summary>
/// Danh sách nhóm giá khách hàng.
/// Dùng string để dễ lưu DB, dễ mở rộng sau này.
/// </summary>
public static class CustomerPriceTiers
{
    /// <summary>
    /// Khách lẻ - dùng giá bán lẻ.
    /// </summary>
    public const string Retail = "RETAIL";

    /// <summary>
    /// Khách sỉ - ưu tiên dùng giá sỉ.
    /// </summary>
    public const string Wholesale = "WHOLESALE";
}