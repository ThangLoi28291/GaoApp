using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Services.Orders;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Storefront;

public sealed partial class StorefrontCheckoutService
{
    private async Task<Data> Build(StorefrontQuoteRequest request, CancellationToken ct)
    {
        var input = Normalize(request);
        Require(await db.Stores.AsNoTracking().AnyAsync(x => x.Id == Store && x.IsActive, ct), "Cửa hàng hiện chưa nhận đơn.");
        var settings = await db.StorefrontSettings.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.IsEnabled, ct);
        Require(settings is not null && (input.PaymentMethod == StorefrontPaymentMethod.Cod ? settings.CodEnabled : settings.QrEnabled), "Cửa hàng chưa mở phương thức thanh toán đã chọn. Hãy chủ động chọn phương thức khác nếu phù hợp.");
        var directory = await addresses.GetAsync(ct);
        var province = directory.Provinces.SingleOrDefault(x => x.Code == input.ProvinceCode);
        var ward = province?.Wards.SingleOrDefault(x => x.Code == input.WardCode);
        Require(province is not null && ward is not null, "Hãy chọn tỉnh/thành và phường/xã thuộc đúng danh mục địa chỉ.");
        var recipient = new StorefrontRecipient(input.RecipientName, input.RecipientPhone, province!.Code, province.Name,
            ward!.Code, ward.Name, input.StreetAddress, input.Note);
        Require(FullAddress(recipient).Length <= 1200, "Địa chỉ nhận hàng quá dài.");
        var priced = await Price(input.Items, ct);
        var sources = await Sources(settings!, ct);
        var warehouses = sources.Select(x => x.WarehouseId).ToList(); var variants = priced.Lines.Select(x => x.VariantId).Distinct().ToList();
        var inventory = await db.InventoryBalances.AsNoTracking().Where(x => x.StoreId == Store && warehouses.Contains(x.WarehouseId) && variants.Contains(x.ProductVariantId)).ToListAsync(ct);
        var shortage = Shortage(priced.Lines, sources, inventory);
        Require(settings!.AllowNegativeInventory || !shortage, "Không đủ tồn khả dụng theo chính sách cửa hàng. Hãy giảm số lượng hoặc liên hệ cửa hàng.");
        var zones = await db.StorefrontShippingZones.AsNoTracking().Where(x => x.StoreId == Store && x.IsActive && x.CountryCode == "VN" &&
            (x.ProvinceCode == "" || x.ProvinceCode == recipient.ProvinceCode) && (x.WardCode == "" || x.WardCode == recipient.WardCode)).ToListAsync(ct);
        // A configured local service area takes precedence; otherwise use the most specific valid nationwide area.
        var zone = zones.OrderBy(x => x.DeliveryMethod == StorefrontDeliveryMethod.Local ? 0 : 1)
            .ThenByDescending(x => x.WardCode.Length > 0 ? 2 : x.ProvinceCode.Length > 0 ? 1 : 0).ThenBy(x => x.Id).FirstOrDefault();
        Require(zone is not null, "Địa chỉ này hiện chưa có phương án giao hàng được cấu hình.");
        var rate = await db.StorefrontShippingRates.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.ShippingZoneId == zone!.Id && x.IsCurrent, ct);
        Require(rate is not null && rate.ByWeight == settings.ShippingByWeight, "Bảng phí giao chưa sẵn sàng. Cửa hàng cần kiểm tra cấu hình.");
        var shipping = StorefrontShippingPolicy.Quote(Store, priced.Goods, priced.Lines.Select(x => new StorefrontShippingLine(x.Quantity, x.WeightKg)).ToList(),
            new(Store, rate!.Id, rate.Revision, rate.ByWeight, rate.BaseFee, rate.AdditionalFee, rate.FirstWeightKg, rate.AdditionalStepKg, rate.FreeFromGoodsAmount));
        var cohort = await db.Stores.AsNoTracking().Where(x => x.Id == Store).Select(x => new { x.IsMultiLegalEntityEnabled, x.MultiLegalEntityActivatedAtUtc }).SingleAsync(ct);
        return new(input, recipient, priced.Lines, shipping.GoodsAmount, shipping.ShippingCharge, shipping.TotalDue, shipping.TotalWeightKg,
            settings.Revision, Convert.ToBase64String(settings.RowVersion), rate.Id, rate.Revision, zone!.DeliveryMethod, zone.Name,
            zone.MinDeliveryDays, zone.MaxDeliveryDays, directory.Fingerprint, settings.AllowNegativeInventory, sources, shortage,
            input.PaymentMethod == StorefrontPaymentMethod.Qr ? await BankBinding(settings, sources, priced.Lines, shipping.TotalDue, inventory, ct) : null,
            cohort.IsMultiLegalEntityEnabled, cohort.IsMultiLegalEntityEnabled ? cohort.MultiLegalEntityActivatedAtUtc : null);
    }

    private static StorefrontQuoteRequest Normalize(StorefrontQuoteRequest request)
    {
        Require(Enum.IsDefined(request.PaymentMethod), "Phương thức thanh toán không hợp lệ.");
        Require(request.Items is not null && request.Items.Count is > 0 and <= 100 && request.Items.All(x => x is not null && x.ProductId > 0 && x.VariantId > 0 && x.UnitId > 0 &&
            x.Quantity > 0 && x.Quantity <= 1_000_000m && decimal.Round(x.Quantity, 4) == x.Quantity) &&
            request.Items.Select(x => (x.VariantId, x.UnitId)).Distinct().Count() == request.Items.Count,
            "Chọn từ 1 đến 100 lựa chọn khác nhau, số lượng dương tối đa 1.000.000 và bốn số lẻ.");
        static string Text(string? value) => Regex.Replace((value ?? "").Trim(), @"\s+", " ");
        var input = new StorefrontQuoteRequest { PaymentMethod=request.PaymentMethod, RecipientName = Text(request.RecipientName), RecipientPhone = Regex.Replace(request.RecipientPhone ?? "", @"[ .()\-]", ""),
            ProvinceCode = (request.ProvinceCode ?? "").Trim().ToUpperInvariant(), WardCode = (request.WardCode ?? "").Trim().ToUpperInvariant(),
            StreetAddress = Text(request.StreetAddress), Note = string.IsNullOrWhiteSpace(request.Note) ? null : Text(request.Note),
            Items = request.Items!.OrderBy(x => x.VariantId).ThenBy(x => x.UnitId).Select(x => new StorefrontCheckoutSelection {
                ProductId = x.ProductId, VariantId = x.VariantId, UnitId = x.UnitId, Quantity = x.Quantity }).ToList() };
        StorefrontProductValidation.Validate(input);
        Require(!string.IsNullOrWhiteSpace(input.RecipientName) && !string.IsNullOrWhiteSpace(input.StreetAddress) &&
            Regex.IsMatch(input.RecipientPhone, @"^\+?[0-9]{9,15}$") && !input.RecipientName.Any(char.IsControl) &&
            !input.StreetAddress.Any(char.IsControl) && !(input.Note?.Any(char.IsControl) ?? false), "Hãy nhập tên, số điện thoại và địa chỉ nhận hàng hợp lệ.");
        return input;
    }

    private async Task<(List<StorefrontCheckoutLine> Lines, decimal Goods)> Price(List<StorefrontCheckoutSelection> selections, CancellationToken ct)
    {
        var products = new Dictionary<int, StorefrontCatalogProduct?>();
        async Task<StorefrontCatalogProduct?> Product(int id)
        { if (!products.TryGetValue(id, out var value)) { value = await catalog.GetAsync(id, ct); products.Add(id, value); } return value; }
        var sourceIds = selections.Select(x => x.VariantId).ToList();
        var sourceVariants = await db.ProductVariants.AsNoTracking().Include(x => x.Product).ThenInclude(x => x.BaseUnit)
            .Where(x => x.StoreId == Store && sourceIds.Contains(x.Id)).ToListAsync(ct);
        // Pricing-only indexes keep existing gift/combo engine identities stable; this graph is never attached to EF.
        var pricingOrder = new Order { StoreId = Store, Id = 0, POSShiftId = 0 };
        var choices = new Dictionary<(int Variant, int Unit), (StorefrontCatalogProduct Product, StorefrontCatalogVariant Variant, StorefrontCatalogUnit Unit)>();
        foreach (var choice in selections)
        {
            var product = await Product(choice.ProductId); var variant = product?.Variants.SingleOrDefault(x => x.VariantId == choice.VariantId);
            var unit = variant?.Units.SingleOrDefault(x => x.UnitId == choice.UnitId);
            var source = sourceVariants.SingleOrDefault(x => x.Id == choice.VariantId && x.ProductId == choice.ProductId && x.Product.StoreId == Store);
            Require(product is not null && variant is not null && unit is not null && source is not null, "Một lựa chọn đã bị ẩn hoặc ngừng bán. Hãy kiểm tra lại giỏ.");
            Require(unit!.Name.Length <= 200 && unit.RetailPrice >= 0 && unit.RetailPrice < StorefrontValues.MoneyLimit && decimal.Round(unit.RetailPrice, 2) == unit.RetailPrice,
                "Giá hoặc tên đơn vị của sản phẩm vượt giới hạn.");
            choices.Add((choice.VariantId, choice.UnitId), (product!, variant!, unit));
            pricingOrder.Lines.Add(new() { Id = pricingOrder.Lines.Count + 1, StoreId = Store, ProductId = choice.ProductId, VariantId = choice.VariantId,
                Variant = source, ItemName = product!.Name, SellingUnitId = choice.UnitId, UnitName = unit.Name, SellingUnitName = unit.Name,
                ProductUnitConversionId = unit.ProductUnitConversionId, BaseUnitId = source!.Product.BaseUnitId, BaseUnitName = source.Product.BaseUnit.Name,
                Quantity = choice.Quantity, Multiplier = unit.BaseMultiplier, BaseQuantity = Base(choice.Quantity, unit.BaseMultiplier), UnitPrice = unit.RetailPrice });
        }
        var productPromotions = await promotionRepository.GetActiveProductDiscountPromotionsAsync(Store, ct);
        var combos = await promotionRepository.GetActiveComboPromotionsAsync(Store, ct);
        Require(productPromotions.Count + combos.Count <= 1000 && productPromotions.Sum(x => x.Items.Count) + combos.Sum(x => x.ComboRules.Count) <= 10000,
            "Số chương trình khuyến mãi vượt giới hạn kiểm tra. Cửa hàng cần rà lại cấu hình.");
        async Task<bool> Eligible(int productId, int? variantId, int? conversionId)
        {
            var product = await Product(productId);
            return product is not null && product.Variants.Any(v => (!variantId.HasValue || v.VariantId == variantId) &&
                v.Units.Any(u => !conversionId.HasValue || u.ProductUnitConversionId == conversionId));
        }
        var eligibleProducts = new List<Promotion>();
        foreach (var promotion in productPromotions.Where(x => x.StoreId == Store))
        {
            var usable = new List<PromotionItem>();
            foreach (var item in promotion.Items.Where(x => !x.IsDeleted && x.StoreId == Store))
                if (await Eligible(item.ProductId, item.VariantId, item.ProductUnitConversionId)) usable.Add(item);
            if (usable.Count == 0) continue;
            promotion.Items = usable; eligibleProducts.Add(promotion);
        }
        var eligibleCombos = new List<Promotion>();
        foreach (var promotion in combos.Where(x => x.StoreId == Store))
        {
            // Mixed pools may include optional hidden SKUs; only actual cart/output choices need publication.
            // Required-item combos remain complete: the shared engine itself refuses absent required choices.
            var valid = promotion.ComboRules.Count > 0 && promotion.ComboRules.All(rule => rule.StoreId == Store && !rule.IsDeleted);
            if (valid) eligibleCombos.Add(promotion);
        }
        var templates = pricingOrder.Lines.ToList();
        static Order Clone(int store, IReadOnlyList<OrderLine> values) => new() { StoreId = store, Id = 0, POSShiftId = 0,
            Lines = values.Select(line => new OrderLine { Id = line.Id, StoreId = line.StoreId, ProductId = line.ProductId, VariantId = line.VariantId,
                Variant = line.Variant, ItemName = line.ItemName, UnitName = line.UnitName, SellingUnitId = line.SellingUnitId,
                SellingUnitName = line.SellingUnitName, ProductUnitConversionId = line.ProductUnitConversionId, BaseUnitId = line.BaseUnitId,
                BaseUnitName = line.BaseUnitName, Quantity = line.Quantity, Multiplier = line.Multiplier, BaseQuantity = line.BaseQuantity, UnitPrice = line.UnitPrice }).ToList() };
        var outputValid = false;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            pricingOrder = Clone(Store, templates);
            await promotionEngine.ApplyOrderPromotionsAsync(pricingOrder, eligibleProducts, eligibleCombos, ct);
            var invalid = pricingOrder.Lines.Where(line => !line.IsDeleted && (line.SellingUnitId is not > 0 ||
                !choices.ContainsKey((line.VariantId, line.SellingUnitId.Value)) || line.Quantity <= 0 || line.Quantity > 1_000_000m || decimal.Round(line.Quantity, 4) != line.Quantity)).ToList();
            if (invalid.Count == 0) { outputValid = true; break; }
            var rejected = invalid.Select(line => line.GiftPromotionId ?? line.PromotionId ?? line.ComboPromotionId).Where(id => id.HasValue).Select(id => id!.Value).ToHashSet();
            Require(rejected.Count > 0 && eligibleProducts.RemoveAll(program => rejected.Contains(program.Id)) + eligibleCombos.RemoveAll(program => rejected.Contains(program.Id)) > 0,
                "Kết quả ưu đãi chưa hợp lệ cho website. Cửa hàng cần kiểm tra chương trình.");
        }
        Require(outputValid, "Chưa tìm được tập ưu đãi hợp lệ sau giới hạn kiểm tra. Cửa hàng cần rà lại chương trình.");
        var lines = pricingOrder.Lines.Where(x => !x.IsDeleted).ToList();
        Require(lines.Count is > 0 and <= 200, "Ưu đãi sinh quá nhiều dòng hàng. Hãy giảm lựa chọn trong giỏ.");
        var next = selections.Count + 1;
        foreach (var line in lines) if (line.Id <= 0) line.Id = next++;
        OrderTotalsPolicy.Recalculate(pricingOrder);
        StorefrontValues.Money(pricingOrder.GrandTotal); Require(pricingOrder.GrandTotal > 0, "Bản đầu chưa nhận đơn có tổng tiền bằng 0.");
        var approved = DeliveryPricingPolicy.ApprovePos(lines.Select(x => new DeliveryPosPriceLine(x.Id, x.Quantity, x.UnitPrice, x.LineDiscount,
            x.PromotionDiscount, x.LineTotal, x.IsPromotionGift, x.PromotionType == PromotionType.ComboFixedPrice)).ToList(), pricingOrder.Subtotal,
            pricingOrder.OrderDiscount, pricingOrder.ComboDiscountTotal, pricingOrder.VoucherDiscountTotal, pricingOrder.GrandTotal).ToDictionary(x => x.LineId);
        var usedPromotions = eligibleProducts.Concat(eligibleCombos).ToDictionary(x => x.Id);
        var result = new List<StorefrontCheckoutLine>();
        foreach (var line in lines.OrderBy(x => x.Id))
        {
            Require(line.SellingUnitId is > 0 && choices.TryGetValue((line.VariantId, line.SellingUnitId.Value), out _), "Ưu đãi có lựa chọn quà chưa được đăng website.");
            var choice = choices[(line.VariantId, line.SellingUnitId!.Value)]; var price = approved[line.Id];
            var ids = new[] { line.PromotionId, line.ComboPromotionId, line.GiftPromotionId, pricingOrder.ComboPromotionId }.Where(x => x.HasValue).Select(x => x!.Value).Distinct().Order().ToArray();
            var promotionSnapshot = Serialize(new { RoleVersion = 1, line.PromotionId, line.PromotionType, line.ComboPromotionId, line.GiftPromotionId,
                line.LineDiscount, OrderSubtotal = pricingOrder.Subtotal, OrderDiscount = pricingOrder.OrderDiscount, OrderComboDiscount = pricingOrder.ComboDiscountTotal,
                OrderVoucherDiscount = pricingOrder.VoucherDiscountTotal, OrderComboPromotionId = pricingOrder.ComboPromotionId,
                Programs = ids.Where(usedPromotions.ContainsKey).Select(id => new {
                Id = id, Version = Convert.ToBase64String(usedPromotions[id].RowVersion), usedPromotions[id].Type,
                Items = usedPromotions[id].Items.OrderBy(x => x.Id).Select(x => new { x.Id, Version = Convert.ToBase64String(x.RowVersion) }),
                Rules = usedPromotions[id].ComboRules.OrderBy(x => x.Id).Select(x => new { x.Id, Version = Convert.ToBase64String(x.RowVersion) }) }),
                line.PromotionDiscount, line.ComboAllocatedDiscount, HeaderDiscountAllocated = price.AllocatedOrderDiscount, line.GiftSourceLineId });
            var adjustment = price.Gross - decimal.Round(line.Quantity * line.UnitPrice, 0, MidpointRounding.AwayFromZero);
            Require(Math.Abs(adjustment) <= 201 && decimal.Truncate(adjustment) == adjustment, "Chênh lệch làm tròn vượt giới hạn.");
            result.Add(new(line.Id, line.ProductId, line.VariantId, line.SellingUnitId.Value, line.ProductUnitConversionId,
                choice.Product.Name, choice.Variant.Name, choice.Unit.Name, line.Quantity, line.Multiplier, Base(line.Quantity, line.Multiplier),
                line.UnitPrice, price.Gross, price.LineDiscount + price.AllocatedOrderDiscount, adjustment, price.Net, choice.Unit.WeightKg,
                line.IsPromotionGift, line.GiftPromotionName ?? line.ComboPromotionName ?? line.PromotionName, promotionSnapshot, line.BaseUnitId, line.BaseUnitName));
        }
        Require(result.Sum(x => x.NetAmount) == pricingOrder.GrandTotal, "Phân bổ tiền hàng không khớp tổng POS.");
        return (result, pricingOrder.GrandTotal);
    }
    private static decimal Base(decimal quantity, decimal multiplier)
    {
        var value = decimal.Round(checked(quantity * multiplier), 4, MidpointRounding.AwayFromZero);
        StorefrontValues.Quantity(value, positive: true); return value;
    }
}
