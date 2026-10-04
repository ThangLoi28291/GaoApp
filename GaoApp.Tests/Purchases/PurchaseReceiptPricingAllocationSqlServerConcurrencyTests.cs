using System.Net;
using System.Net.Http.Json;
using GaoApp.Domain.Enums;
using GaoApp.Tests.Security;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Purchases;

[Collection("R1FinalDatabasePreflight")]
public sealed class PurchaseReceiptPricingAllocationSqlServerConcurrencyTests
{
    [Fact]
    public async Task Concurrent_create_and_apply_have_one_winner_and_physical_or_conversion_changes_block_apply()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        var seed = await PricingAllocationTestData.Seed(app, store);
        using var admin = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var url = PricingAllocationTestData.Url(seed.ReceiptId);
        var request = PricingAllocationTestData.Request(await PricingAllocationTestData.Get(admin, url));
        var saves = await Task.WhenAll(admin.Http.PutAsJsonAsync(url, request), admin.Http.PutAsJsonAsync(url, request));
        Assert.Single(saves, x => x.IsSuccessStatusCode); Assert.Single(saves, x => x.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in saves) response.Dispose();
        var saved = await PricingAllocationTestData.Get(admin, url);
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var receipt = await db.StockDocuments.AsNoTracking().SingleAsync(x => x.Id == seed.ReceiptId);
        var inventoryCount = await db.InventoryTransactions.CountAsync();
        object ConfirmPayload(string version) => new
        {
            rowVersion = version, supplierId = receipt.SupplierId, acceptPriceVariance = true,
            lines = saved.PhysicalLines.Select(x => new
            { stockDocumentLineId = x.StockDocumentLineId, unitPriceBeforeVat = 999999m }).ToArray()
        };
        var apply = new { saved.ReceiptRowVersion, saved.PlanRowVersion };
        var applies = await Task.WhenAll(admin.Http.PostAsJsonAsync(url + "/apply", apply), admin.Http.PostAsJsonAsync(url + "/apply", apply));
        Assert.Single(applies, x => x.IsSuccessStatusCode); Assert.Single(applies, x => x.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in applies) response.Dispose();
        Assert.Equal(1, await db.PurchaseReceiptPricingPlans.CountAsync());
        Assert.Equal(PurchaseReceiptPricingPlanState.Applied, (await db.PurchaseReceiptPricingPlans.SingleAsync()).State);
        Assert.False(await db.PurchasePayables.AnyAsync());
        var applied = await PricingAllocationTestData.Get(admin, url);
        var line = await db.StockDocumentLines.FirstAsync(x => x.StockDocumentId == seed.ReceiptId);
        line.Quantity++; line.BaseQuantity += line.Factor; await db.SaveChangesAsync();
        using (var stale = await admin.Http.PostAsJsonAsync(url + "/apply", new { applied.ReceiptRowVersion, applied.PlanRowVersion })) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var staleConfirm = await admin.Http.PostAsJsonAsync(
            $"/admin/api/stock-documents/{seed.ReceiptId}/approve-commercial", ConfirmPayload(applied.ReceiptRowVersion)))
            Assert.Equal(HttpStatusCode.Conflict, staleConfirm.StatusCode);
        Assert.Equal(inventoryCount, await db.InventoryTransactions.CountAsync());
        Assert.False(await db.PurchasePayables.AnyAsync());
        Assert.Equal(StockDocumentStatus.PendingApproval,
            (await db.StockDocuments.AsNoTracking().SingleAsync(x => x.Id == seed.ReceiptId)).Status);
        var workspace = await PricingAllocationTestData.Get(admin, url); Assert.True(workspace.IsStale);
        saved = PricingAllocationTestData.Read(await admin.JsonAsync(HttpMethod.Put, url, PricingAllocationTestData.Request(workspace)));
        var conversion = await db.ProductUnitConversions.Include(x => x.Unit).SingleAsync(x => x.Id == seed.CartonId);
        conversion.Unit.Name += " mới";
        await db.SaveChangesAsync();
        using (var stale = await admin.Http.PostAsJsonAsync(url + "/apply", new { saved.ReceiptRowVersion, saved.PlanRowVersion })) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.True((await PricingAllocationTestData.Get(admin, url)).IsStale);
        conversion.IsActive = false; await db.SaveChangesAsync();
        using (var stale = await admin.Http.PostAsJsonAsync(url + "/apply", new { saved.ReceiptRowVersion, saved.PlanRowVersion }))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }
}
