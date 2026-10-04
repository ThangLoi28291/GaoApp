using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Entities;
using GaoApp.Tests.Security;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Purchases;

[Collection("R1FinalDatabasePreflight")]
public sealed class PurchaseReceiptPricingAllocationServiceTests
{
    [Fact]
    public async Task Viewing_preview_and_save_do_not_write_prices_apply_preserves_exact_amount_and_does_not_post()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var seed = await PricingAllocationTestData.Seed(app, store);
        using var admin = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var url = PricingAllocationTestData.Url(seed.ReceiptId);
        var workspace = await PricingAllocationTestData.Get(admin, url);
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        Assert.False(await db.PurchaseReceiptPricingPlans.AnyAsync());
        var before = await db.StockDocumentLines.AsNoTracking().Where(x => x.StockDocumentId == seed.ReceiptId).ToArrayAsync();
        var request = PricingAllocationTestData.Request(workspace, .505m);
        var preview = await admin.JsonAsync(HttpMethod.Post, url + "/preview", request);
        Assert.True(preview.GetProperty("canApply").GetBoolean());
        Assert.False(await db.PurchaseReceiptPricingPlans.AnyAsync());
        var saved = PricingAllocationTestData.Read(await PricingAllocationTestData.Send(app, admin, HttpMethod.Put, url, request));
        Assert.Equal(PurchaseReceiptPricingPlanState.Draft, saved.State);
        Assert.Equal(2, await db.PurchaseReceiptPricingPlanLines.CountAsync());
        var savedPrices = await db.StockDocumentLines.AsNoTracking().Where(x => x.StockDocumentId == seed.ReceiptId).ToArrayAsync();
        Assert.Equal(before.Select(x => x.LineTotal), savedPrices.Select(x => x.LineTotal));
        var inventoryCount = await db.InventoryTransactions.CountAsync();
        var applied = PricingAllocationTestData.Read(await admin.JsonAsync(HttpMethod.Post, url + "/apply", new PurchaseReceiptPricingApplyRequest
        { ReceiptRowVersion = saved.ReceiptRowVersion, PlanRowVersion = saved.PlanRowVersion! }));
        Assert.Equal(PurchaseReceiptPricingPlanState.Applied, applied.State);
        Assert.False(applied.IsStale);
        var amounts = await db.StockDocumentLines.AsNoTracking().Where(x => x.StockDocumentId == seed.ReceiptId).ToArrayAsync();
        Assert.All(amounts, x => { Assert.Equal(1.01m, x.LineTotal); Assert.Equal(.51m, x.UnitPriceBeforeVat); });
        Assert.Equal(inventoryCount, await db.InventoryTransactions.CountAsync());
        Assert.False(await db.PurchasePayables.AnyAsync());
        // Saving a revised helper draft does not change the current Goods prices.
        var revised = PricingAllocationTestData.Request(applied, .6m);
        var resaved = PricingAllocationTestData.Read(await admin.JsonAsync(HttpMethod.Put, url, revised));
        Assert.Equal(PurchaseReceiptPricingPlanState.Draft, resaved.State);
        Assert.Equal(2, await db.PurchaseReceiptPricingPlanLines.CountAsync());
        Assert.All(await db.StockDocumentLines.AsNoTracking().Where(x => x.StockDocumentId == seed.ReceiptId).ToArrayAsync(), x => Assert.Equal(1.01m, x.LineTotal));
    }

    [Fact]
    public async Task Different_sku_gift_uses_confirmed_history_converted_to_gift_unit_and_keeps_saved_value_fixed()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        var seed = await PricingAllocationTestData.Seed(app, store);
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var original = await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == store.VariantId);
        var gift = new ProductVariant { StoreId = store.StoreId, ProductId = original.ProductId, Sku = "GIFT", ProductVariantName = "Quà khác SKU" };
        db.Add(gift); await db.SaveChangesAsync();
        var pack = await db.ProductUnitConversions.SingleAsync(x => x.Id == seed.PackId);
        var conversion = new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = gift.Id, UnitId = pack.UnitId, Factor = 4m };
        db.Add(conversion);
        db.Add(new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = gift.Id,
            UnitId = original.Product.BaseUnitId, Factor = 1m });
        await db.SaveChangesAsync();
        var giftLine = await db.StockDocumentLines.SingleAsync(x => x.StockDocumentId == seed.ReceiptId && x.Factor == 4m);
        giftLine.ProductVariantId = gift.Id; giftLine.ProductUnitConversionId = conversion.Id;
        var warehouse = await db.Warehouses.SingleAsync(x => x.Id == store.WarehouseId);
        var historical = new StockDocument { StoreId = store.StoreId, WarehouseId = store.WarehouseId, DocumentNo = "GIFT-HISTORY",
            Status = StockDocumentStatus.Confirmed, ConfirmedLegalEntityId = warehouse.LegalEntityId, ConfirmedAtUtc = DateTime.UtcNow.AddDays(-1),
            Lines = [new() { LineNo = 1, ProductVariantId = gift.Id, UnitId = pack.UnitId, ProductUnitConversionId = conversion.Id,
                Factor = 4m, Quantity = 2m, BaseQuantity = 8m, LineTotal = 100m, UnitCost = 50m, UnitPriceBeforeVat = 50m, ProductNameSnapshot = "Quà khác SKU" }] };
        db.Add(historical); await db.SaveChangesAsync();
        using var admin = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var url = PricingAllocationTestData.Url(seed.ReceiptId);
        var workspace = await PricingAllocationTestData.Get(admin, url);
        var request = PricingAllocationTestData.Request(workspace);
        var target = workspace.PhysicalLines.Single(x => x.ProductVariantId == gift.Id);
        var source = workspace.PhysicalLines.Single(x => x.ProductVariantId == store.VariantId);
        request.Lines.Single(x => x.StockDocumentLineId == target.StockDocumentLineId).BillQuantity = 1m;
        request.ActualBillTotal = 300m;
        request.Rules = [new() { RuleKey = "gift", Type = PurchaseReceiptPricingRuleType.Gift, GiftMode = PurchaseReceiptPricingGiftMode.DifferentSku,
            SourceLineIds = [source.StockDocumentLineId], GiftLineId = target.StockDocumentLineId, GiftUnitId = target.UnitId, GiftQuantity = 1m }];
        request.GiftValuations = [new() { ProductVariantId = gift.Id, UnitId = target.UnitId, ManualUnitValueBeforeVat = 999m }];
        var automatic = JsonSerializer.Deserialize<PurchaseReceiptPricingAllocationRequest>(JsonSerializer.Serialize(request))!;
        automatic.GiftValuations.Clear();
        automatic.Rules[0].GiftQuantity = .5m;
        automatic.Rules.Add(new() { RuleKey = "base-gift", Type = PurchaseReceiptPricingRuleType.Gift,
            GiftMode = PurchaseReceiptPricingGiftMode.DifferentSku, SourceLineIds = [source.StockDocumentLineId],
            GiftLineId = target.StockDocumentLineId, GiftUnitId = original.Product.BaseUnitId, GiftQuantity = 2m });
        var forward = await admin.JsonAsync(HttpMethod.Post, url + "/preview", automatic);
        automatic.Rules.Reverse();
        var reverse = await admin.JsonAsync(HttpMethod.Post, url + "/preview", automatic);
        Assert.True(forward.GetProperty("canApply").GetBoolean());
        Assert.Equal(original.Product.BaseUnitId, forward.GetProperty("giftValues")[0].GetProperty("unitId").GetInt32());
        Assert.Equal(forward.GetRawText(), reverse.GetRawText());
        Assert.False(await db.PurchaseReceiptPricingPlans.AnyAsync());
        var saved = PricingAllocationTestData.Read(await admin.JsonAsync(HttpMethod.Put, url, request));
        Assert.Equal(50m, saved.Preview!.GiftValues.Single().UnitValueBeforeVat);
        Assert.Equal(PurchaseReceiptGiftValuationSource.LatestConfirmedPurchase, saved.Preview.GiftValues.Single().Source);
        historical.Lines.Single().LineTotal = 200m; await db.SaveChangesAsync();
        var applied = PricingAllocationTestData.Read(await admin.JsonAsync(HttpMethod.Post, url + "/apply", new { saved.ReceiptRowVersion, saved.PlanRowVersion }));
        Assert.Equal(50m, applied.Preview!.GiftValues.Single().UnitValueBeforeVat);
        Assert.Equal(150m, applied.Preview.Lines.Single(x => x.StockDocumentLineId == target.StockDocumentLineId).FinalAmountBeforeVat);
        Assert.Equal(150m, applied.Preview.Lines.Single(x => x.StockDocumentLineId == source.StockDocumentLineId).FinalAmountBeforeVat);
    }

    [Fact]
    public async Task Same_sku_gift_and_discount_graph_persist_and_replay_without_price_history()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        var seed = await PricingAllocationTestData.Seed(app, store);
        using var admin = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var url = PricingAllocationTestData.Url(seed.ReceiptId);
        var workspace = await PricingAllocationTestData.Get(admin, url);
        var request = PricingAllocationTestData.Request(workspace);
        var pack = workspace.PhysicalLines.Single(x => x.Factor == 4m);
        var carton = workspace.PhysicalLines.Single(x => x.Factor == 24m);
        request.Lines.Single(x => x.StockDocumentLineId == pack.StockDocumentLineId).BillQuantity = 1.75m;
        request.ActualBillTotal = 355m;
        request.Rules = [new() { RuleKey = "buy7gift1", Type = PurchaseReceiptPricingRuleType.Gift, GiftMode = PurchaseReceiptPricingGiftMode.SameSku,
            GiftLineId = pack.StockDocumentLineId, GiftUnitId = pack.UnitId, GiftQuantity = .25m, SourceLineIds = [pack.StockDocumentLineId] },
            new() { RuleKey = "discount", Type = PurchaseReceiptPricingRuleType.PercentageDiscount, DiscountPercent = 10m, SourceLineIds = [carton.StockDocumentLineId] }];
        var saved = PricingAllocationTestData.Read(await admin.JsonAsync(HttpMethod.Put, url, request));
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(2, await db.PurchaseReceiptPricingRules.CountAsync()); Assert.Equal(2, await db.PurchaseReceiptPricingRuleSources.CountAsync());
        Assert.Equal(PurchaseReceiptGiftValuationSource.SameSkuBlend, (await db.PurchaseReceiptGiftValuations.SingleAsync()).Source);
        var applied = PricingAllocationTestData.Read(await admin.JsonAsync(HttpMethod.Post, url + "/apply", new { saved.ReceiptRowVersion, saved.PlanRowVersion }));
        Assert.Equal(355m, applied.Preview!.SystemTotal);
        Assert.Equal(175m, applied.Preview.Lines.Single(x => x.StockDocumentLineId == pack.StockDocumentLineId).FinalAmountBeforeVat);
        Assert.All(await db.PurchaseReceiptPricingRuleSources.ToArrayAsync(), x => Assert.Equal(saved.PlanId, x.PricingPlanId));
        var edit = PricingAllocationTestData.Request(applied); edit.Lines.Single(x => x.StockDocumentLineId == pack.StockDocumentLineId).BillQuantity = 1.75m;
        edit.Rules = request.Rules; edit.ActualBillTotal = request.ActualBillTotal;
        await admin.JsonAsync(HttpMethod.Put, url, edit);
        Assert.Equal(2, await db.PurchaseReceiptPricingRuleSources.CountAsync());
    }

    [Fact]
    public async Task Group_discount_residual_evidence_is_persisted_and_replayed_on_apply()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0]; var seed = await PricingAllocationTestData.Seed(app, store);
        using var admin = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var url = PricingAllocationTestData.Url(seed.ReceiptId);
        var request = PricingAllocationTestData.Request(await PricingAllocationTestData.Get(admin, url), .525m);
        request.ActualBillTotal = 1.89m;
        request.Rules = [new() { RuleKey = "group", Type = PurchaseReceiptPricingRuleType.PercentageDiscount,
            DiscountPercent = 10m, SourceLineIds = request.Lines.Select(x => x.StockDocumentLineId).Reverse().ToList() }];
        var saved = PricingAllocationTestData.Read(await admin.JsonAsync(HttpMethod.Put, url, request));
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var rule = await db.PurchaseReceiptPricingRules.AsNoTracking().Include(x => x.Sources)
            .ThenInclude(x => x.PricingPlanLine).SingleAsync();
        Assert.Equal(.21m, rule.Amount);
        Assert.Equal(rule.Amount, rule.Sources.Sum(x => x.Amount));
        var sources = rule.Sources.OrderBy(x => x.PricingPlanLine.LineNo).ToArray();
        Assert.Equal(.11m, sources[0].Amount); Assert.Equal(.01m, sources[0].ResidualAmount);
        Assert.Equal(.10m, sources[1].Amount); Assert.Equal(0m, sources[1].ResidualAmount);
        var applied = PricingAllocationTestData.Read(await admin.JsonAsync(HttpMethod.Post, url + "/apply",
            new { saved.ReceiptRowVersion, saved.PlanRowVersion }));
        Assert.Equal(1.89m, applied.Preview!.SystemTotal);
        Assert.Contains(applied.Preview.Residuals, x => x.Stage == "discount" && x.Amount == .01m);
        Assert.Equal(1.89m, (await db.StockDocuments.AsNoTracking().SingleAsync(x => x.Id == seed.ReceiptId)).SubtotalBeforeVat);
        Assert.False(await db.PurchasePayables.AnyAsync());
    }

    [Fact]
    public async Task Mismatched_bill_can_be_saved_as_draft_but_apply_is_blocked_without_price_or_state_changes()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0]; var seed = await PricingAllocationTestData.Seed(app, store);
        using var admin = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var url = PricingAllocationTestData.Url(seed.ReceiptId);
        var request = PricingAllocationTestData.Request(await PricingAllocationTestData.Get(admin, url), 100m);
        request.ActualBillTotal++;
        var saved = PricingAllocationTestData.Read(await admin.JsonAsync(HttpMethod.Put, url, request));
        Assert.False(saved.Preview!.CanApply);
        using var rejected = await admin.Http.PostAsJsonAsync(url + "/apply", new { saved.ReceiptRowVersion, saved.PlanRowVersion });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(PurchaseReceiptPricingPlanState.Draft, (await db.PurchaseReceiptPricingPlans.SingleAsync()).State);
        Assert.All(await db.StockDocumentLines.ToArrayAsync(), x => Assert.Equal(20m, x.LineTotal));
        Assert.False(await db.PurchasePayables.AnyAsync());
    }
}

internal static class PricingAllocationTestData
{
    internal static async Task<JsonElement> Send(FullApplicationFixture app, FullApplicationFixture.Client client, HttpMethod method, string url, object body)
    {
        try { return await client.JsonAsync(method, url, body); }
        catch (Exception error)
        {
            var output = (IEnumerable<string>)typeof(FullApplicationFixture).GetField("output", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(app)!;
            throw new InvalidOperationException(error.Message + "\n" + string.Join("\n", output.TakeLast(60)), error);
        }
    }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    internal static string Url(int id) => $"/admin/api/stock-documents/{id}/pricing-allocation";
    internal static PurchaseReceiptPricingAllocationWorkspace Read(JsonElement json)
        => json.Deserialize<PurchaseReceiptPricingAllocationWorkspace>(Json)!;
    internal static async Task<PurchaseReceiptPricingAllocationWorkspace> Get(FullApplicationFixture.Client session, string url)
        => Read(await session.JsonAsync(HttpMethod.Get, url));
    internal static async Task<ReceiptBarcodeProposalSqlServerTests.Seed> Seed(FullApplicationFixture app, FullApplicationFixture.StoreSeed store)
    {
        var seed = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, store);
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        (await db.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId)).Status = StockDocumentStatus.PendingApproval;
        await db.SaveChangesAsync();
        return seed;
    }
    internal static PurchaseReceiptPricingAllocationRequest Request(PurchaseReceiptPricingAllocationWorkspace workspace, decimal price = 100m)
        => new()
        {
            ReceiptRowVersion = workspace.ReceiptRowVersion, PlanRowVersion = workspace.PlanRowVersion,
            ActualBillTotal = decimal.Round(workspace.PhysicalLines.Sum(x => x.Quantity * price), 2, MidpointRounding.AwayFromZero),
            Lines = workspace.PhysicalLines.Select(x => new PurchaseReceiptPricingLineInput
            { StockDocumentLineId = x.StockDocumentLineId, RowVersion = x.RowVersion, BillUnitId = x.UnitId, BillQuantity = x.Quantity, BillUnitPriceBeforeVat = price }).ToList()
        };
}
