using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Storefront;
using GaoApp.Storefront.Web.Models;

namespace GaoApp.Storefront.Web.Services;

// Read-only estimates. WEB04 independently validates prices, availability, shipping and totals inside placement.
public sealed class StorefrontCartReader(IStorefrontCatalogService catalog)
{
    public const decimal MaximumQuantity = 1_000_000m; // Same bound as POS.
    public async Task<StorefrontCartResponse> ResolveAsync(StorefrontCartRequest request, CancellationToken ct = default)
    {
        if (request.Items is null || request.Items.Count > 100 || request.Items.Any(x => x is null || x.ProductId <= 0 || x.VariantId <= 0 || x.UnitId <= 0 ||
            x.Quantity <= 0 || x.Quantity > MaximumQuantity || decimal.Round(x.Quantity, 4) != x.Quantity) ||
            request.Items.Select(x => (x.VariantId, x.UnitId)).Distinct().Count() != request.Items.Count)
            throw new ValidationAppException("Giỏ tối đa 100 lựa chọn khác nhau; số lượng dương, không quá 1.000.000 và tối đa bốn số lẻ.");
        var products = new Dictionary<int, StorefrontCatalogProduct?>();
        var result = new List<StorefrontCartLine>();
        decimal estimate = 0;
        foreach (var selection in request.Items)
        {
            if (!products.TryGetValue(selection.ProductId, out var product))
            {
                product = await catalog.GetAsync(selection.ProductId, ct);
                products.Add(selection.ProductId, product);
            }
            var variant = product?.Variants.SingleOrDefault(x => x.VariantId == selection.VariantId);
            var unit = variant?.Units.SingleOrDefault(x => x.UnitId == selection.UnitId);
            if (unit is null)
            {
                result.Add(new(selection.ProductId, selection.VariantId, selection.UnitId, selection.Quantity, false,
                    "Lựa chọn này đã ẩn hoặc không còn được bán. Hãy xóa khỏi giỏ hoặc chọn lại.", null, null, null, null, null, null));
                continue;
            }
            decimal amount;
            try { amount = decimal.Round(checked(unit.RetailPrice * selection.Quantity), 0, MidpointRounding.AwayFromZero); estimate = checked(estimate + amount); }
            catch (OverflowException) { throw new ValidationAppException("Số tiền vượt giới hạn. Hãy giảm số lượng trong giỏ."); }
            if (amount < 0 || amount >= StorefrontValues.MoneyLimit || estimate < 0 || estimate >= StorefrontValues.MoneyLimit)
                throw new ValidationAppException("Tạm tính vượt giới hạn tiền của hệ thống. Hãy giảm số lượng trong giỏ.");
            result.Add(new(selection.ProductId, selection.VariantId, selection.UnitId, selection.Quantity, true, "Giá và trạng thái vừa được kiểm tra lại.",
                product!.Name, variant!.Name, unit.Name, unit.RetailPrice, amount, product.Media.FirstOrDefault(x => x.Kind == StorefrontMediaKind.Image)?.Path));
        }
        return new(result, estimate, result.Count > 0 && result.All(x => x.Available), "Đây là tạm tính chưa gồm phí giao. Giá, ưu đãi và tổng tiền được chốt rõ ở bước đặt hàng.");
    }
}
