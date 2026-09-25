using System.Net;
using System.Text.RegularExpressions;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class PosRewardEligibilitySqlServerTests
{
    [Fact]
    public async Task Real_pos_earns_only_retail_base_units_and_returns_reverse_the_recorded_earning()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var seed = await SeedAsync(app, store);
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var shift = await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        var shiftId = shift.GetProperty("id").GetInt32();

        var retail = await SellAsync(2, seed.BaseConversion, 12000, 12000);
        var autoPack = await SellAsync(4, seed.BaseConversion, 20000, 0, addOneAtATime: true);
        await SellAsync(1, seed.PackConversion, 20000, 0);

        // Excluding a parent through the actual settings form excludes its child at checkout.
        var settingsHtml = await client.Http.GetStringAsync("/admin/reward-vouchers/settings");
        using (var saved = await client.Http.PostAsync("/admin/reward-vouchers/settings", SettingsForm(settingsHtml, seed.ParentCategory)))
            Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        await SellAsync(1, seed.BaseConversion, 6000, 0);

        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var conversion = await db.ProductUnitConversions.SingleAsync(x => x.Id == seed.BaseConversion);
            conversion.Price = 9000;
            await db.SaveChangesAsync();
        }
        // A changed price/category must not erase the points to reverse for an earlier sale.
        await ReturnAsync(retail, 6000);
        await ReturnAsync(retail, 6000);
        await ReturnAsync(autoPack, 5000);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var deductions = await db.CustomerRewardLedgers.Where(x => x.OrderId == retail && x.Type == CustomerRewardLedgerType.ReturnDeducted).ToListAsync();
            Assert.Equal(2, deductions.Count);
            Assert.Equal(-12000, deductions.Sum(x => x.Amount));
            Assert.Empty(await db.CustomerRewardLedgers.Where(x => x.OrderId == autoPack).ToListAsync());
            Assert.Equal(0, await db.CustomerRewardLedgers.Where(x => x.CustomerId == seed.CustomerId).SumAsync(x => x.Amount));
        }

        // Removing the exclusion restores eligibility at the new full retail price.
        settingsHtml = await client.Http.GetStringAsync("/admin/reward-vouchers/settings");
        using (var saved = await client.Http.PostAsync("/admin/reward-vouchers/settings", SettingsForm(settingsHtml)))
            Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        await SellAsync(1, seed.BaseConversion, 9000, 9000);

        async Task<int> SellAsync(int qty, int conversion, decimal payable, decimal earned, bool addOneAtATime = false)
        {
            var draft = await client.JsonAsync(HttpMethod.Post, $"/admin/pos/draft?customerId={seed.CustomerId}");
            var id = draft.GetProperty("orderId").GetInt32();
            for (var i = 0; i < (addOneAtATime ? qty : 1); i++)
                await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{id}/items?variantId={store.VariantId}&productUnitConversionId={conversion}&qty={(addOneAtATime ? 1 : qty)}");
            var body = new { orderId = id, clientRequestId = Guid.NewGuid(), method = 0, amount = payable };
            for (var retry = 0; retry < 2; retry++)
            {
                var result = await client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/payment-and-finalize", body);
                Assert.True(result.GetProperty("finalized").GetBoolean());
            }
            await using var db = app.Database.CreateTenantContext(store.StoreId);
            var order = await db.Orders.Include(x => x.Lines).SingleAsync(x => x.Id == id);
            Assert.Equal(OrderStatus.Completed, order.Status);
            Assert.Equal(payable, order.GrandTotal);
            Assert.All(order.Lines, x => Assert.NotNull(x.RewardBaseUnitPrice));
            Assert.All(order.Lines, x => Assert.NotNull(x.RewardableAmountSnapshot));
            Assert.Equal(earned, order.Lines.Sum(x => x.RewardableAmountSnapshot));
            var ledger = await db.CustomerRewardLedgers.Where(x => x.OrderId == id && x.Type == CustomerRewardLedgerType.SaleEarned).ToListAsync();
            Assert.Equal(earned, ledger.Sum(x => x.Amount));
            Assert.Equal(earned > 0 ? 1 : 0, ledger.Count);
            return id;
        }

        async Task ReturnAsync(int orderId, decimal price)
        {
            // The existing return-number generator has one-second precision.
            // Space separate returns to exercise loyalty independently of that legacy constraint.
            await Task.Delay(1100);
            int lineId;
            await using (var db = app.Database.CreateTenantContext(store.StoreId))
                lineId = (await db.OrderLines.FirstAsync(x => x.OrderId == orderId)).Id;
            var result = await client.JsonAsync(HttpMethod.Post, "/admin/pos/returns", new {
                orderId, posShiftId = shiftId, type = (int)SalesReturnType.ReturnAndRefund, reason = "Reward eligibility regression",
                lines = new[] { new { orderLineId = lineId, returnQuantity = 1, returnBaseQuantity = 1, refundUnitAmount = price, action = (int)SalesReturnLineAction.Restock } },
                payments = new[] { new { method = (int)PaymentMethod.Cash, amount = price } } });
            Assert.True(result.GetProperty("success").GetBoolean());
        }
    }

    [Fact]
    public async Task Category_settings_require_permission_and_antiforgery_and_reject_other_store_or_stale_selection()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var seed = await SeedAsync(app, store);
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        using var viewer = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View));
        var html = await client.Http.GetStringAsync("/admin/reward-vouchers/settings");
        Assert.Contains("rewardCategoryList", html);
        var evidencePath = Path.Combine(FullApplicationFixture.SourceRoot(), "Logs", "reward-eligibility-tests", "settings.html");
        Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
        await File.WriteAllTextAsync(evidencePath, html);
        int foreignId;
        await using (var db = app.Database.CreateTenantContext(app.Stores[1].StoreId)) foreignId = (await db.Categories.FirstAsync()).Id;

        using (var denied = await viewer.Http.GetAsync("/admin/reward-vouchers/settings"))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await viewer.Http.PostAsync("/admin/reward-vouchers/settings", SettingsForm(html, seed.ParentCategory)))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var token = client.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();
        client.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using (var denied = await client.Http.PostAsync("/admin/reward-vouchers/settings", SettingsForm(html, seed.ParentCategory)))
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        client.Http.DefaultRequestHeaders.Add("RequestVerificationToken", token);

        using (var invalid = await client.Http.PostAsync("/admin/reward-vouchers/settings", SettingsForm(html, foreignId)))
        {
            Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
            Assert.Contains("không hợp lệ", WebUtility.HtmlDecode(await invalid.Content.ReadAsStringAsync()));
        }
        using (var saved = await client.Http.PostAsync("/admin/reward-vouchers/settings", SettingsForm(html, seed.ParentCategory)))
            Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        using (var stale = await client.Http.PostAsync("/admin/reward-vouchers/settings", SettingsForm(html)))
        {
            Assert.Equal(HttpStatusCode.OK, stale.StatusCode);
            Assert.Contains("người khác thay đổi", WebUtility.HtmlDecode(await stale.Content.ReadAsStringAsync()));
        }
        await using var verify = app.Database.CreateTenantContext(store.StoreId);
        Assert.False((await verify.Categories.SingleAsync(x => x.Id == seed.ParentCategory)).IsRewardEligible);
        Assert.Empty(await verify.CustomerRewardLedgers.ToListAsync());
    }

    private static FormUrlEncodedContent SettingsForm(string html, params int[] excluded)
    {
        string Value(string name) => WebUtility.HtmlDecode(Regex.Match(html, $"name=\"{name}\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
        var values = new List<KeyValuePair<string, string>> {
            new("MoneyPerPoint", "1000"), new("PointsPerVoucher", "100"), new("VoucherValue", "10000"),
            new("IsEnabled", "true"), new("RowVersion", Value("RowVersion")),
            new("UpdateCategoryExclusions", "true"), new("CategorySelectionVersion", Value("CategorySelectionVersion")) };
        values.AddRange(excluded.Select(id => new KeyValuePair<string, string>("ExcludedCategoryIds", id.ToString())));
        return new(values);
    }

    private sealed record Seed(int CustomerId, int BaseConversion, int PackConversion, int ParentCategory);
    private static async Task<Seed> SeedAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var product = await db.Products.SingleAsync();
        var variant = await db.ProductVariants.SingleAsync(x => x.Id == store.VariantId);
        product.BasePrice = 6000;
        variant.Price = 6000;
        product.IsRewardEligibleOverride = true;
        var parent = new Category { StoreId = store.StoreId, Code = "MILK", Name = "Sữa", IsActive = true };
        var category = await db.Categories.SingleAsync(x => x.Id == product.CategoryId);
        category.Name = "Sữa tươi";
        category.Parent = parent;
        var packUnit = new Unit { StoreId = store.StoreId, Code = "PACK", Name = "Lốc", IsActive = true };
        var baseUnit = new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = store.VariantId,
            UnitId = product.BaseUnitId, Factor = 1, IsBaseUnit = true, IsDefaultForSale = true, Price = 6000 };
        var pack = new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = store.VariantId,
            Unit = packUnit, Factor = 4, Price = 20000 };
        var customer = new Customer { StoreId = store.StoreId, Name = "Reward regression", Code = "REWARD-TEST" };
        db.AddRange(parent, packUnit, baseUnit, pack, customer,
            new RewardSettings { StoreId = store.StoreId, IsEnabled = true, MoneyPerPoint = 1000, PointsPerVoucher = 100, VoucherValue = 10000 });
        await db.SaveChangesAsync();
        return new(customer.Id, baseUnit.Id, pack.Id, parent.Id);
    }
}
