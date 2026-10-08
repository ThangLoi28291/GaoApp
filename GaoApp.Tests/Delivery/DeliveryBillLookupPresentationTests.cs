using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Application.DTOs.POS;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Tests.Security;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Delivery;

[Collection("DeliveryD02")]
public sealed class DeliveryBillLookupPresentationTests(DeliveryD02Fixture fixture)
{
    [Fact]
    public async Task Saved_golden_prices_and_promotion_are_identical_in_receipt_and_source_bill()
    {
        using var c = await DeliveryBillPresentationSeed.CreateAsync(fixture.Web);
        var receipt = await c.Client.Http.GetFromJsonAsync<OrderReceiptDto>($"/admin/pos/orders/{c.OrderId}/receipt");
        Assert.NotNull(receipt);
        Assert.Equal(494000m, receipt.Subtotal);
        Assert.Equal(58333m, receipt.PromotionDiscountTotal);
        Assert.Equal(435667m, receipt.GrandTotal);
        var milk = Assert.Single(receipt.Lines, x => x.ItemName == "Sữa đôi");
        Assert.Equal(2m, milk.Quantity); Assert.Equal(95000m, milk.UnitPrice);
        Assert.Equal(190000m, milk.GrossAmount); Assert.Equal(23333m, milk.PromotionDiscount);
        Assert.Equal(166667m, milk.LineTotal); Assert.Equal("Giảm sữa <saved>", milk.PromotionName);
        var single = Assert.Single(receipt.Lines, x => x.ItemName == "Sữa đơn");
        Assert.Equal(95000m, single.GrossAmount); Assert.Equal(11667m, single.PromotionDiscount);
        Assert.Equal(83333m, single.LineTotal);
        var html = await c.Client.Http.GetStringAsync(c.BillUrl);
        var text = WebUtility.HtmlDecode(html);
        Assert.Contains("494.000", text); Assert.Contains("58.333", text); Assert.Contains("435.667", text);
        Assert.Contains("190.000", text); Assert.Contains("23.333", text); Assert.Contains("166.667", text);
        Assert.Contains("95.000", text); Assert.Contains("11.667", text); Assert.Contains("83.333", text);
        Assert.Contains("Giảm sữa <saved>", text); Assert.Contains("&lt;saved&gt;", html);
        Assert.DoesNotContain("Giảm sữa <saved>", html);
    }

    [Fact]
    public async Task Saved_header_components_are_read_once_without_repricing_or_changing_the_archive()
    {
        using var c = await DeliveryBillPresentationSeed.CreateAsync(fixture.Web, headerDiscounts: true);
        var before = await DeliveryBillPresentationSeed.SnapshotAsync(fixture.Web, c);
        await using var catalog = fixture.Web.Database.CreateTenantContext(c.Account.Store.StoreId);
        var priorPrice = await catalog.ProductVariants.Where(x => x.Id == c.Account.Store.VariantId).Select(x => x.Price).SingleAsync();
        try
        {
            // The current catalog is deliberately different from the saved sale price.
            var variant = await catalog.ProductVariants.SingleAsync(x => x.Id == c.Account.Store.VariantId);
            variant.Price = 777m; await catalog.SaveChangesAsync();
            var receipt = await c.Client.Http.GetFromJsonAsync<OrderReceiptDto>($"/admin/pos/orders/{c.OrderId}");
            Assert.NotNull(receipt);
            Assert.Equal(494000m, receipt.Subtotal); Assert.Equal(2000m, receipt.LineDiscountTotal);
            Assert.Equal(58333m, receipt.PromotionDiscountTotal); Assert.Equal(5000m, receipt.ComboDiscountTotal);
            Assert.Equal(6000m, receipt.OrderDiscount); Assert.Equal(7000m, receipt.VoucherDiscountTotal);
            Assert.Equal(415667m, receipt.GrandTotal);
            Assert.Equal(receipt.GrandTotal, receipt.Subtotal - receipt.DiscountTotal);
            Assert.Equal(receipt.DiscountTotal, receipt.LineDiscountTotal + receipt.PromotionDiscountTotal +
                receipt.ComboDiscountTotal + receipt.OrderDiscount + receipt.VoucherDiscountTotal);
            Assert.Equal(95000m, Assert.Single(receipt.Lines, x => x.ItemName == "Sữa đôi").UnitPrice);
            Assert.Equal(5000m, Assert.Single(receipt.Lines, x => x.ItemName == "Bánh").ComboAllocatedDiscount);
            var bill = await c.Client.Http.GetStringAsync(c.BillUrl);
            var detail = await c.Client.Http.GetStringAsync($"/admin/pos/order-detail/{c.OrderId}");
            Assert.Contains("415.667", WebUtility.HtmlDecode(bill));
            Assert.Contains("415.667", WebUtility.HtmlDecode(detail));
            var breakdown = Regex.Match(WebUtility.HtmlDecode(bill), "class=\"delivery-bill-breakdown\"[\\s\\S]*?</dl>");
            Assert.True(breakdown.Success);
            foreach (var label in new[] { "Giảm giá dòng hàng", "Khuyến mãi sản phẩm", "Giảm combo", "Giảm cấp đơn", "Giảm voucher" })
                Assert.Single(Regex.Matches(breakdown.Value, Regex.Escape(label)).Cast<Match>());
            Assert.Equal(before, await DeliveryBillPresentationSeed.SnapshotAsync(fixture.Web, c));
        }
        finally
        {
            catalog.ChangeTracker.Clear();
            var variant = await catalog.ProductVariants.SingleAsync(x => x.Id == c.Account.Store.VariantId);
            variant.Price = priorPrice; await catalog.SaveChangesAsync();
        }
        Assert.Equal(priorPrice, await catalog.ProductVariants.AsNoTracking().Where(x => x.Id == c.Account.Store.VariantId).Select(x => x.Price).SingleAsync());
    }

    [Fact]
    public async Task Pending_delivery_is_visible_in_lookup_full_detail_and_both_reprint_routes()
    {
        using var c = await DeliveryBillPresentationSeed.CreateAsync(fixture.Web);
        var list = await c.Client.JsonAsync(HttpMethod.Get, $"/admin/pos/orders?keyword={c.Number}");
        var item = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal("Cancelled", item.GetProperty("status").GetString()); // stored archival state is retained
        Assert.Equal("DeliveryPending", item.GetProperty("displayStatus").GetString());
        Assert.Equal(c.DeliveryId, item.GetProperty("delivery").GetProperty("deliveryOrderId").GetInt32());
        var receipt = await c.Client.Http.GetFromJsonAsync<OrderReceiptDto>($"/admin/pos/orders/{c.OrderId}/receipt");
        Assert.NotNull(receipt); Assert.Equal("DeliveryPending", receipt.DisplayStatus);
        Assert.NotNull(receipt.Delivery); Assert.True(receipt.Delivery.IsPendingSettlement);
        Assert.False(receipt.Delivery.IsCancelled); Assert.Equal(c.BillUrl, receipt.Delivery.BillUrl);
        var full = WebUtility.HtmlDecode(await c.Client.Http.GetStringAsync($"/admin/pos/order-detail/{c.OrderId}"));
        Assert.Contains(c.Code, full); Assert.Contains(c.BillUrl, full); Assert.Contains("Chưa chốt", full, StringComparison.OrdinalIgnoreCase);
        foreach (var path in new[] { $"/admin/pos/orders/{c.OrderId}/print", $"/admin/pos/receipt/{c.OrderId}" })
        {
            using var response = await c.Client.Http.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal(c.BillUrl, response.Headers.Location?.OriginalString);
        }
        var first = await c.Client.Http.GetStringAsync(c.BillUrl);
        var second = await c.Client.Http.GetStringAsync(c.BillUrl);
        Assert.Equal(first, second);
        Assert.Contains("CHƯA THU TIỀN", WebUtility.HtmlDecode(first));
    }

    [Fact]
    public async Task Cancelled_delivery_remains_cancelled_and_foreign_store_cannot_read_its_overlay_or_bill()
    {
        using var c = await DeliveryBillPresentationSeed.CreateAsync(fixture.Web, state: DeliveryState.Cancelled);
        var receipt = await c.Client.Http.GetFromJsonAsync<OrderReceiptDto>($"/admin/pos/orders/{c.OrderId}/receipt");
        Assert.NotNull(receipt); Assert.Equal("DeliveryCancelled", receipt.DisplayStatus);
        Assert.NotNull(receipt.Delivery); Assert.True(receipt.Delivery.IsCancelled);
        Assert.False(receipt.Delivery.IsPendingSettlement);
        Assert.Contains("ĐÃ HỦY", WebUtility.HtmlDecode(await c.Client.Http.GetStringAsync(c.BillUrl)));
        var outsider = await fixture.Web.AddAccountAsync(fixture.Web.Stores[1], "*");
        using var client = await fixture.Web.LoginAsync(outsider);
        var list = await client.JsonAsync(HttpMethod.Get, $"/admin/pos/orders?keyword={c.Number}&deliveryOnly=true");
        Assert.Empty(list.GetProperty("items").EnumerateArray());
        using var bill = await client.Http.GetAsync(c.BillUrl);
        Assert.Equal(HttpStatusCode.NotFound, bill.StatusCode);
        await using var db = fixture.Web.Database.CreateTenantContext(outsider.Store.StoreId);
        Assert.Null(await new GaoApp.Infrastructure.Repositories.Orders.OrderRepository(db).GetDeliveryInfoAsync(c.OrderId));
    }

    [Fact]
    public async Task Approved_picking_total_stays_primary_with_original_source_breakdown_separate()
    {
        using var c = await DeliveryBillPresentationSeed.CreateAsync(fixture.Web, state: DeliveryState.ReadyForHandover);
        await using (var db = fixture.Web.Database.CreateTenantContext(c.Account.Store.StoreId))
        {
            var now = DateTime.UtcNow;
            db.DeliveryPickingWorks.Add(new DeliveryPickingWork { StoreId = c.Account.Store.StoreId, DeliveryOrderId = c.DeliveryId,
                PickerUserId = c.Account.UserId, AssignedAtUtc = now, StartedAtUtc = now, SubmittedAtUtc = now,
                ApprovalRequired = true, Reason = "Thiếu hàng", CustomerConfirmationNote = "Khách đồng ý lượng đã duyệt",
                ApprovedRevision = 1, ApprovedByUserId = c.Account.UserId, ApprovedAtUtc = now, ApprovedTotal = 166667m });
            var quotes = await db.DeliveryOrderLines.Where(x => x.DeliveryOrderId == c.DeliveryId).OrderBy(x => x.Id).ToListAsync();
            var approvedQuantities = new[] { 1m, 1m, 0m, 0m };
            var nets = new[] { 83334m, 83333m, 0m, 0m };
            for (var i = 0; i < quotes.Count; i++)
                db.DeliveryPickingLines.Add(new DeliveryPickingLine { StoreId = c.Account.Store.StoreId, DeliveryOrderId = c.DeliveryId,
                    DeliveryOrderLineId = quotes[i].Id, PlannedQuantity = quotes[i].OrderedQuantity, ReportedQuantity = approvedQuantities[i],
                    ReporterUserId = c.Account.UserId, ReportedAtUtc = now, ReportFactKind = "picker-report",
                    ShortageReason = approvedQuantities[i] < quotes[i].OrderedQuantity ? "Thiếu hàng" : null,
                    ApprovedQuantity = approvedQuantities[i], ApprovedOriginalCoverage = 0, ApprovedNet = nets[i] });
            await db.SaveChangesAsync();
        }
        var text = WebUtility.HtmlDecode(await c.Client.Http.GetStringAsync(c.BillUrl));
        var main = Regex.Match(text, "class=\"delivery-bill-total\"[\\s\\S]*?</div>");
        Assert.True(main.Success); Assert.Contains("Giá trị lượng đã duyệt", main.Value);
        Assert.Contains("166.667", main.Value); Assert.DoesNotContain("435.667", main.Value);
        Assert.Contains("435.667", text); Assert.Contains("58.333", text);
    }
}

// Controlled persisted read facts, independent of transfer/pricing/posting executors.
// All rows belong to the existing GUID fixture; source prices are saved before the immutable delivery link.
internal static class DeliveryBillPresentationSeed
{
    internal sealed record Case(FullApplicationFixture.Account Account, FullApplicationFixture.Client Client,
        int OrderId, int DeliveryId, string Number, string Code) : IDisposable
    {
        internal string BillUrl => $"/admin/deliveries/{DeliveryId}/bill";
        public void Dispose() => Client.Dispose();
    }

    internal static async Task<Case> CreateAsync(FullApplicationFixture web, bool headerDiscounts = false,
        DeliveryState state = DeliveryState.Created, int storeIndex = 0, bool linkDelivery = true)
    {
        var store = web.Stores[storeIndex];
        var suffix = Guid.NewGuid().ToString("N")[..12];
        await using (var db = web.Database.CreateTenantContext(store.StoreId))
        {
            var terminal = new POSTerminal { StoreId = store.StoreId, Code = "PRES-" + suffix, Name = "Presentation terminal" };
            db.POSTerminals.Add(terminal); await db.SaveChangesAsync();
            store = store with { TerminalId = terminal.Id };
        }
        var account = await web.AddAccountAsync(store, "*");
        await using var seed = web.Database.CreateTenantContext(store.StoreId);
        var variant = await seed.ProductVariants.SingleAsync(x => x.Id == store.VariantId);
        var warehouse = await seed.Warehouses.SingleAsync(x => x.Id == store.WarehouseId);
        var shift = new POSShift { StoreId = store.StoreId, TerminalId = store.TerminalId, WarehouseId = store.WarehouseId,
            OpenedByUserId = account.UserId, Status = POSShiftStatus.Open, ShiftCode = "PRES-" + suffix };
        var order = new Order { StoreId = store.StoreId, POSShift = shift, OrderNumber = "PRES-" + suffix, Status = OrderStatus.Cancelled,
            PaymentStatus = PaymentStatus.Unpaid, Subtotal = 494000m, PromotionDiscountTotal = 58333m,
            DiscountTotal = headerDiscounts ? 78333m : 58333m, ComboDiscountTotal = headerDiscounts ? 5000m : 0,
            OrderDiscount = headerDiscounts ? 6000m : 0, VoucherDiscountTotal = headerDiscounts ? 7000m : 0,
            ComboPromotionName = headerDiscounts ? "Combo đã lưu" : null, GrandTotal = headerDiscounts ? 415667m : 435667m };
        var names = new[] { "Sữa đôi", "Sữa đơn", "Bánh", "Hàng thường" };
        var prices = new[] { 95000m, 95000m, 100000m, 109000m };
        var quantities = new[] { 2m, 1m, 1m, 1m };
        var promotions = new[] { 23333m, 11667m, 23333m, 0m };
        for (var i = 0; i < names.Length; i++)
        {
            var manual = headerDiscounts && i == 3 ? 2000m : 0;
            order.Lines.Add(new OrderLine { StoreId = store.StoreId, ProductId = variant.ProductId, VariantId = variant.Id,
                ItemName = names[i], UnitName = "Cái", Quantity = quantities[i], BaseQuantity = quantities[i], Multiplier = 1,
                UnitPrice = prices[i], OriginalUnitPrice = prices[i], PromotionDiscount = promotions[i], LineDiscount = manual,
                PromotionName = promotions[i] > 0 ? "Giảm sữa <saved>" : null,
                ComboAllocatedDiscount = headerDiscounts && i == 2 ? 5000m : 0,
                LineTotal = quantities[i] * prices[i] - promotions[i] - manual });
        }
        seed.Orders.Add(order); await seed.SaveChangesAsync();
        var delivery = new DeliveryOrder { StoreId = store.StoreId, Code = "GH-PRES-" + suffix,
            LookupToken = Guid.NewGuid().ToString("N").ToUpperInvariant(), State = state,
            SourceCartId = order.Id, SourceWarehouseId = store.WarehouseId, SourceLegalEntityId = warehouse.LegalEntityId,
            CreatedShiftId = shift.Id, CreatedTerminalId = store.TerminalId, CreatedByUserId = account.UserId,
            RecipientName = "Người nhận đã lưu", RecipientPhone = "0900000001", RecipientAddress = "Địa chỉ thử nghiệm",
            QuotedTotal = order.GrandTotal };
        if (linkDelivery)
        {
            foreach (var line in order.Lines)
            {
                var header = headerDiscounts && line.ItemName == names[3] ? 18000m : 0;
                delivery.Lines.Add(new DeliveryOrderLine { StoreId = store.StoreId, SourceCartId = order.Id, SourceOrderLineId = line.Id,
                    VariantId = line.VariantId, ItemName = line.ItemName, UnitName = "Cái", BaseUnitName = "Cái",
                    OrderedQuantity = line.Quantity, BaseMultiplier = 1, UnitPrice = line.UnitPrice,
                    Gross = line.Quantity * line.UnitPrice, LineDiscount = line.LineDiscount + line.PromotionDiscount,
                    AllocatedOrderDiscount = header, Net = line.LineTotal - header });
            }
            seed.DeliveryOrders.Add(delivery); await seed.SaveChangesAsync();
        }
        var client = await web.LoginAsync(account);
        return new(account, client, order.Id, delivery.Id, order.OrderNumber!, delivery.Code);
    }

    internal static async Task<string> SnapshotAsync(FullApplicationFixture web, Case c)
    {
        await using var db = web.Database.CreateTenantContext(c.Account.Store.StoreId);
        var order = await db.Orders.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == c.OrderId);
        return JsonSerializer.Serialize(new { order.Status, order.RowVersion, order.Subtotal, order.DiscountTotal,
            order.PromotionDiscountTotal, order.ComboDiscountTotal, order.OrderDiscount, order.VoucherDiscountTotal, order.GrandTotal,
            Lines = order.Lines.OrderBy(x => x.Id).Select(x => new { x.Id, x.RowVersion, x.Quantity, x.UnitPrice,
                x.LineDiscount, x.PromotionDiscount, x.ComboAllocatedDiscount, x.LineTotal }).ToArray() });
    }
}
