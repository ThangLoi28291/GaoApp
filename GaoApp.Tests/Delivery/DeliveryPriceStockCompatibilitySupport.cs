using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Application.DTOs.Promotions;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Tests.Inventory;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Delivery;

// Own catalog rows, actual POS pricing endpoints, and the existing disposable SQL fixture.
// Unlike D04's controlled price fixture, this helper never writes cart prices or discounts.
internal sealed class DeliveryPriceStockCase(DeliveryD02Case source, int firstVariant, int secondVariant,
    int firstConversion, int promotionId) : IDisposable
{
    internal DeliveryD02Case Source => source;
    internal int FirstVariant => firstVariant;
    internal int SecondVariant => secondVariant;
    internal int PromotionId => promotionId;
    internal int? VoucherId { get; private set; }
    internal int? CustomerId { get; private set; }
    internal DeliveryD04Case Picking => new(source);
    internal DeliveryD05Case Inventory => new(Picking);
    internal Order PricedOrder { get; private set; } = null!;

    internal static async Task<DeliveryPriceStockCase> PrepareAsync(DeliveryD02Fixture fixture,
        string kind = "none", decimal quantity = 3m, decimal stock = 20m)
    {
        var source = await fixture.CaseAsync(false);
        try
        {
            int a, b, ca, cb, productId;
            await using (var db = source.Context())
            {
                var template = await db.ProductVariants.Include(x => x.Product)
                    .SingleAsync(x => x.Id == source.Account.Store.VariantId);
                var suffix = Guid.NewGuid().ToString("N")[..12];
                var product = new Product { StoreId = template.StoreId, Name = "Compatibility " + suffix,
                    Alias = "compat-" + suffix, CategoryId = template.Product.CategoryId,
                    SupplierId = template.Product.SupplierId, BaseUnitId = template.Product.BaseUnitId,
                    BasePrice = 10000, IsActive = true, IsSellable = true };
                db.Products.Add(product); await db.SaveChangesAsync(); productId = product.Id;
                var first = new ProductVariant { StoreId = product.StoreId, ProductId = product.Id,
                    Sku = "COMP-A-" + suffix, ProductVariantName = "Compatibility A", Price = 10000, CostPrice = 5000, IsActive = true };
                var second = new ProductVariant { StoreId = product.StoreId, ProductId = product.Id,
                    Sku = "COMP-B-" + suffix, ProductVariantName = "Compatibility B", Price = 10000, CostPrice = 5000, IsActive = true };
                db.AddRange(first, second); await db.SaveChangesAsync(); a = first.Id; b = second.Id;
                var baseA = new ProductUnitConversion { StoreId = product.StoreId, ProductVariantId = a,
                    UnitId = product.BaseUnitId, Factor = 1, Price = 10000, IsBaseUnit = true, IsDefaultForSale = true };
                var baseB = new ProductUnitConversion { StoreId = product.StoreId, ProductVariantId = b,
                    UnitId = product.BaseUnitId, Factor = 1, Price = 10000, IsBaseUnit = true, IsDefaultForSale = true };
                db.AddRange(baseA, baseB); await db.SaveChangesAsync(); ca = baseA.Id; cb = baseB.Id;
                if (kind == "mixed")
                {
                    var unit = new Unit { StoreId = product.StoreId, Code = "C6-" + suffix, Name = "Compatibility six " + suffix, IsActive = true };
                    db.Units.Add(unit); await db.SaveChangesAsync();
                    var pack = new ProductUnitConversion { StoreId = product.StoreId, ProductVariantId = b,
                        UnitId = unit.Id, Factor = 6, Price = 60000, IsActive = true };
                    db.ProductUnitConversions.Add(pack); await db.SaveChangesAsync(); cb = pack.Id;
                }
                if (stock > 0)
                    foreach (var variant in new[] { a, b })
                        await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(
                            new InventoryMovementFactory().CreatePurchaseReceipt(source.Account.Store.WarehouseId,
                                variant, stock, 5000, "COMP-INITIAL-" + source.CartId, variant, "Owned synthetic stock", variant));
            }
            var promotion = 0;
            if (kind != "none")
            {
                var request = new SavePromotionRequest { Name = "Compatibility " + kind + " " + source.CartId,
                    Type = kind is "combo" or "mixed" ? PromotionType.ComboFixedPrice : kind == "gift" ? PromotionType.BuyXGetY : PromotionType.ProductDiscount,
                    DiscountType = PromotionDiscountType.Percentage, DiscountValue = kind == "line" ? 20 : 0,
                    ComboFixedPrice = kind == "combo" ? 15000 : kind == "mixed" ? 20000 : null,
                    ComboPricingMode = kind == "mixed" ? ComboPricingMode.MixedQuantity : ComboPricingMode.RequiredItems,
                    ComboQuantity = kind == "mixed" ? 3 : null,
                    BuyQuantity = kind == "gift" ? 2 : null, GetQuantity = kind == "gift" ? 1 : null,
                    RequireGiftQuantityInCart = true, IsActive = true,
                    StartAtUtc = DateTime.UtcNow.AddDays(-1), EndAtUtc = DateTime.UtcNow.AddDays(1), Priority = 100 };
                if (kind is "combo" or "mixed")
                    request.ComboRules = [new() { ProductId = productId, VariantId = a, RequiredQuantity = 1 },
                        new() { ProductId = productId, VariantId = b, RequiredQuantity = 1 }];
                else request.Items = [new() { ProductId = productId, VariantId = a, MinQuantity = 1 }];
                var saved = await source.Client.JsonAsync(HttpMethod.Post, "/admin/promotion/save", request);
                Assert.True(saved.GetProperty("success").GetBoolean(), saved.ToString());
                promotion = saved.GetProperty("id").GetInt32();
            }
            var result = new DeliveryPriceStockCase(source, a, b, ca, promotion);
            await result.ClearInitialLineAsync();
            // POS quantities are paid goods; the engine adds a separate zero-price gift.
            await result.AddAsync(a, ca, kind == "combo" ? 1 : kind == "mixed" ? 1.5m : kind == "gift" ? 2 : quantity);
            if (kind is "combo" or "mixed") await result.AddAsync(b, cb, kind == "combo" ? 1 : .25m);
            await result.ReloadPriceAsync();
            return result;
        }
        catch { source.Dispose(); throw; }
    }

    private async Task ClearInitialLineAsync()
    {
        await using var db = source.Context();
        var lineId = await db.OrderLines.Where(x => x.OrderId == source.CartId && !x.IsDeleted).Select(x => x.Id).SingleAsync();
        await source.Client.JsonAsync(HttpMethod.Delete, "/admin/pos/lines/" + lineId);
    }
    private Task<JsonElement> AddAsync(int variant, int conversion, decimal quantity)
        => source.Client.JsonAsync(HttpMethod.Post, "/admin/pos/" + source.CartId + "/items?variantId=" + variant +
            "&productUnitConversionId=" + conversion + "&qty=" + quantity.ToString(CultureInfo.InvariantCulture));
    internal async Task ReloadPriceAsync()
    {
        await using var db = source.Context();
        PricedOrder = await db.Orders.AsNoTracking().Include(x => x.Lines).Include(x => x.RewardVouchers)
            .SingleAsync(x => x.Id == source.CartId);
    }
    internal async Task ApplyVoucherAsync(decimal value = 3000)
    {
        await using (var db = source.Context())
        {
            var customer = new Customer { StoreId = source.Account.Store.StoreId, Name = "Compatibility customer " + source.CartId,
                Phone = "0901234567", PriceTier = "RETAIL" };
            db.Customers.Add(customer); await db.SaveChangesAsync(); CustomerId = customer.Id;
            var voucher = new CustomerRewardVoucher { StoreId = customer.StoreId, CustomerId = customer.Id,
                VoucherCode = "COMP-" + Guid.NewGuid().ToString("N"), Value = value, Status = CustomerRewardVoucherStatus.Available,
                IssuedAtUtc = DateTime.UtcNow, RequiredAmount = 0 };
            db.CustomerRewardVouchers.Add(voucher); await db.SaveChangesAsync(); VoucherId = voucher.Id;
        }
        await AssignVoucherAsync(source, CustomerId!.Value, VoucherId!.Value);
        await ReloadPriceAsync();
    }
    internal static async Task AssignVoucherAsync(DeliveryD02Case cart, int customerId, int voucherId)
    {
        await cart.Client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/customer/" + customerId, new { repriceExistingLines = false });
        await cart.Client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/reward-vouchers", new { voucherIds = new[] { voucherId } });
    }
    internal async Task<DeliveryD02Case> OtherCartAsync(DeliveryD02Fixture fixture, bool applyVoucher = true)
    {
        var other = await fixture.CaseAsync(false);
        try
        {
            await using var db = other.Context();
            var oldLine = await db.OrderLines.Where(x => x.OrderId == other.CartId && !x.IsDeleted).Select(x => x.Id).SingleAsync();
            await other.Client.JsonAsync(HttpMethod.Delete, "/admin/pos/lines/" + oldLine);
            await other.Client.JsonAsync(HttpMethod.Post, "/admin/pos/" + other.CartId + "/items?variantId=" + firstVariant +
                "&productUnitConversionId=" + firstConversion + "&qty=3");
            if (applyVoucher) await AssignVoucherAsync(other, CustomerId!.Value, VoucherId!.Value);
            return other;
        }
        catch { other.Dispose(); throw; }
    }
    internal async Task<DeliveryPosResult> CreateAsync(DeliveryPosCreateRequest? request = null)
    {
        var result = await DeliveryD03Support.Create(source, request);
        source.Detail = result.Delivery; return result;
    }
    internal async Task<DeliveryPickingDetailDto> ReadyAsync(bool partial = false)
    {
        var picking = Picking; var detail = await picking.ClaimAsync();
        await picking.CommandAsync("report", new DeliveryPickingReportRequest(Guid.NewGuid(), detail.Delivery.Version,
            detail.Lines.Select(x => new DeliveryPickingReportLine(x.LineId,
                partial && decimal.Parse(x.UnitPriceText, CultureInfo.InvariantCulture) > 0 ?
                    (decimal.Parse(x.PlannedQuantityText, CultureInfo.InvariantCulture) / 2).ToString(CultureInfo.InvariantCulture) : x.PlannedQuantityText,
                partial && decimal.Parse(x.UnitPriceText, CultureInfo.InvariantCulture) > 0 ? "Khách chỉ nhận một nửa" : null)).ToArray()));
        detail = await picking.SubmitAsync();
        if (detail.Delivery.State == nameof(DeliveryState.AwaitingApproval)) detail = await picking.ApproveAsync();
        return detail;
    }
    internal async Task<string> SnapshotAsync() => await Inventory.SnapshotAsync();
    public void Dispose() => source.Dispose();
}
