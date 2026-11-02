using GaoApp.Application.Storefront;

namespace GaoApp.Storefront.Web.Models;

public sealed record StorefrontPageContext(string DisplayName, string? Description, string ContactPhone, string CartScope)
{
    public string RuntimeMode { get; init; } = "closed";
}
public sealed record StorefrontHomePage(StorefrontCatalogPage Products, IReadOnlyList<string> Groups);
public sealed record StorefrontProductsPage(StorefrontCatalogQuery Query, StorefrontCatalogPage Products, IReadOnlyList<string> Groups);
public sealed class StorefrontCartRequest
{
    public List<StorefrontCartSelection> Items { get; set; } = [];
}
public sealed class StorefrontCartSelection
{
    public int ProductId { get; set; }
    public int VariantId { get; set; }
    public int UnitId { get; set; }
    public decimal Quantity { get; set; }
}
public sealed record StorefrontCartLine(int ProductId, int VariantId, int UnitId, decimal Quantity, bool Available,
    string Message, string? ProductName, string? VariantName, string? UnitName, decimal? UnitPrice, decimal? LineAmount, string? ImagePath)
{
    public string? UnitPriceText => UnitPrice is { } value ? value.ToString("#,0.##", System.Globalization.CultureInfo.GetCultureInfo("vi-VN")) + " đ" : null;
    public string? LineAmountText => LineAmount is { } value ? value.ToString("#,0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN")) + " đ" : null;
}
public sealed record StorefrontCartResponse(IReadOnlyList<StorefrontCartLine> Items, decimal GoodsEstimate, bool CanContinue, string Notice)
{
    public string GoodsEstimateText => GoodsEstimate.ToString("#,0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN")) + " đ";
}
