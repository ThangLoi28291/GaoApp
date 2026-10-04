using System.Net.Http.Json;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Domain.Enums;
using GaoApp.Tests.Security;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Purchases;

[Collection("R1FinalDatabasePreflight")]
public sealed class PurchaseReceiptBillPersistenceTests
{
    [Fact]
    public async Task Fixed_amount_and_partial_sources_replay_through_reload_apply_and_confirm_without_percentage_conversion()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        var seed = await PricingAllocationTestData.Seed(app, store); using var admin = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var url = PricingAllocationTestData.Url(seed.ReceiptId); var workspace = await PricingAllocationTestData.Get(admin, url);
        var pack = workspace.PhysicalLines.Single(x => x.Factor == 4m); var carton = workspace.PhysicalLines.Single(x => x.Factor == 24m);
        var request = PricingAllocationTestData.Request(workspace); request.ActualBillTotal = 355;
        request.BillLines = [new() { BillLineKey = "pack", LineNo = 1, ProductVariantId = pack.ProductVariantId, BillUnitId = pack.UnitId, BillQuantity = 2, BillUnitPriceBeforeVat = 100 },
            new() { BillLineKey = "carton", LineNo = 2, ProductVariantId = pack.ProductVariantId, BillUnitId = carton.UnitId, BillQuantity = 2, BillUnitPriceBeforeVat = 100 }];
        request.Rules = [new() { RuleKey = "fixed", Type = PurchaseReceiptPricingRuleType.FixedAmountDiscount, DiscountAmount = 45,
            BillSources = [new() { BillLineKey = "pack", Quantity = 1 }, new() { BillLineKey = "carton", Quantity = .5m }] }];
        var saved = PricingAllocationTestData.Read(await PricingAllocationTestData.Send(app, admin, HttpMethod.Put, url, request));
        Assert.Equal(355, saved.Preview!.SystemTotal);
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var inventoryBefore = await db.InventoryTransactions.CountAsync();
        var stored = await db.PurchaseReceiptPricingRules.AsNoTracking().SingleAsync();
        Assert.Equal(PurchaseReceiptPricingRuleType.FixedAmountDiscount, stored.Type); Assert.Equal(45, stored.Amount); Assert.Equal(0, stored.DiscountPercent); Assert.Equal("", stored.Name);
        var reloaded = await PricingAllocationTestData.Get(admin, url); var rule = reloaded.Draft.Rules.Single();
        Assert.Equal(45, rule.DiscountAmount); Assert.Equal(0, rule.DiscountPercent);
        Assert.Equal(.5m, rule.BillSources.Single(x => x.BillLineKey == "carton").Quantity);
        var applied = PricingAllocationTestData.Read(await admin.JsonAsync(HttpMethod.Post, url + "/apply", new { reloaded.ReceiptRowVersion, reloaded.PlanRowVersion }));
        Assert.Equal(355, applied.Preview!.SystemTotal); Assert.False(await db.PurchasePayables.AnyAsync());
        var receipt = await db.StockDocuments.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == seed.ReceiptId);
        await admin.JsonAsync(HttpMethod.Post, $"/admin/api/stock-documents/{seed.ReceiptId}/approve-commercial", new
        {
            rowVersion = applied.ReceiptRowVersion, supplierId = receipt.SupplierId, isMerchandisePaid = false, acceptPriceVariance = true,
            lines = receipt.Lines.Select(x => new { stockDocumentLineId = x.Id, unitPriceBeforeVat = 999999m })
        });
        Assert.Equal(355, (await db.PurchasePayables.AsNoTracking().SingleAsync()).Amount);
        var confirmed = await PricingAllocationTestData.Get(admin, url);
        Assert.False(confirmed.CanEdit); Assert.True(confirmed.Preview!.CanApply, string.Join(";", confirmed.Preview.Errors));
        Assert.Equal(355, confirmed.Preview.SystemTotal); Assert.Equal(45, confirmed.Draft.Rules.Single().DiscountAmount);
        Assert.Equal(inventoryBefore + 2, await db.InventoryTransactions.CountAsync());
    }
    [Fact]
    public async Task Bill_rows_participating_quantities_and_gift_line_links_replay_through_apply_confirm_and_readonly_evidence()
    {
        await using var app=await FullApplicationFixture.StartAsync();var store=app.Stores[0];
        var seed=await PricingAllocationTestData.Seed(app,store);using var admin=await app.LoginAsync(await app.AddAccountAsync(store,"*"));
        var url=PricingAllocationTestData.Url(seed.ReceiptId);var workspace=await PricingAllocationTestData.Get(admin,url);
        Assert.Empty(workspace.Draft.BillLines!);
        Assert.Equal(0m, workspace.Draft.ActualBillTotal);
        var pack=workspace.PhysicalLines.Single(x=>x.Factor==4m);var carton=workspace.PhysicalLines.Single(x=>x.Factor==24m);
        var request=PricingAllocationTestData.Request(workspace);request.ActualBillTotal=400;
        request.BillLines=[new(){BillLineKey="a",LineNo=1,ProductVariantId=pack.ProductVariantId,BillUnitId=pack.UnitId,BillQuantity=1,BillUnitPriceBeforeVat=100},
            new(){BillLineKey="b",LineNo=2,ProductVariantId=pack.ProductVariantId,BillUnitId=pack.UnitId,BillQuantity=.75m,BillUnitPriceBeforeVat=100},
            new(){BillLineKey="c",LineNo=3,ProductVariantId=pack.ProductVariantId,BillUnitId=carton.UnitId,BillQuantity=2,BillUnitPriceBeforeVat=112.5m},
            new(){BillLineKey="g",LineNo=4,ProductVariantId=pack.ProductVariantId,BillUnitId=pack.UnitId,BillQuantity=.25m,IsGift=true}];
        request.Rules=[new(){RuleKey="gift",ProgramKey="program",Name="Mua một phần",Type=PurchaseReceiptPricingRuleType.Gift,GiftMode=PurchaseReceiptPricingGiftMode.SameSku,
            GiftLineId=pack.StockDocumentLineId,GiftUnitId=pack.UnitId,GiftQuantity=.25m,GiftBillLineKey="g",
            BillSources=[new(){BillLineKey="a",Quantity=.5m},new(){BillLineKey="b",Quantity=.75m}]}];
        var saved=PricingAllocationTestData.Read(await PricingAllocationTestData.Send(app,admin,HttpMethod.Put,url,request));
        Assert.Equal(new[]{"a","b","c","g"},saved.Draft.BillLines!.Select(x=>x.BillLineKey));Assert.Equal(.5m,saved.Draft.Rules.Single().BillSources.Single(x=>x.BillLineKey=="a").Quantity);
        await using var db=app.Database.CreateTenantContext(store.StoreId);Assert.Equal(4,await db.PurchaseReceiptBillLines.CountAsync());
        Assert.Equal(2,await db.PurchaseReceiptPricingRuleSources.CountAsync());Assert.False(await db.PurchasePayables.AnyAsync());
        var resaved=PricingAllocationTestData.Read(await admin.JsonAsync(HttpMethod.Put,url,saved.Draft));Assert.Equal(4,await db.PurchaseReceiptBillLines.CountAsync());
        var applied=PricingAllocationTestData.Read(await admin.JsonAsync(HttpMethod.Post,url+"/apply",new{resaved.ReceiptRowVersion,resaved.PlanRowVersion}));
        Assert.Equal(400,applied.Preview!.SystemTotal);Assert.False(await db.PurchasePayables.AnyAsync());
        var receipt=await db.StockDocuments.AsNoTracking().Include(x=>x.Lines).SingleAsync(x=>x.Id==seed.ReceiptId);
        var payload=new{rowVersion=applied.ReceiptRowVersion,supplierId=receipt.SupplierId,isMerchandisePaid=false,acceptPriceVariance=true,
            lines=receipt.Lines.Select(x=>new{stockDocumentLineId=x.Id,unitPriceBeforeVat=999999m})};
        await admin.JsonAsync(HttpMethod.Post,$"/admin/api/stock-documents/{seed.ReceiptId}/approve-commercial",payload);
        Assert.Equal(400,(await db.PurchasePayables.AsNoTracking().SingleAsync()).Amount);
        var conversion=await db.ProductUnitConversions.SingleAsync(x=>x.Id==seed.PackId);conversion.IsActive=false;await db.SaveChangesAsync();
        var confirmed=await PricingAllocationTestData.Get(admin,url);Assert.False(confirmed.CanEdit);Assert.Equal(400,confirmed.Preview!.SystemTotal);Assert.True(confirmed.Preview.CanApply,string.Join(";",confirmed.Preview.Errors));
        Assert.Equal(4,confirmed.Draft.BillLines!.Count);
    }
}
