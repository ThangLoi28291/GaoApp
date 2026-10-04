using System.Net;
using System.Net.Http.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Tests.Security;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Purchases;

[Collection("R1FinalDatabasePreflight")]
public sealed class PurchaseReceiptPricingAllocationTenantIsolationTests
{
    [Fact]
    public async Task Approve_permissions_are_OR_scoped_to_current_store_and_foreign_lines_units_cannot_be_saved()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var local = await PricingAllocationTestData.Seed(app, app.Stores[0]);
        var foreign = await PricingAllocationTestData.Seed(app, app.Stores[1]);
        using var purchase = await app.LoginAsync(await app.AddAccountAsync(app.Stores[0], PermissionCodes.Purchase.Receipt.Approve));
        using var inventory = await app.LoginAsync(await app.AddAccountAsync(app.Stores[0], PermissionCodes.Inventory.StockDocument.Approve));
        using var viewer = await app.LoginAsync(await app.AddAccountAsync(app.Stores[0], PermissionCodes.Inventory.StockDocument.View));
        var url = PricingAllocationTestData.Url(local.ReceiptId);
        var workspace = await PricingAllocationTestData.Get(purchase, url);
        Assert.Null((await PricingAllocationTestData.Get(inventory, url)).PlanId);
        using (var denied = await viewer.Http.GetAsync(url)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Post, HttpMethod.Put })
        {
            using var request = new HttpRequestMessage(method, PricingAllocationTestData.Url(foreign.ReceiptId) + (method == HttpMethod.Post ? "/preview" : ""));
            if (method != HttpMethod.Get) request.Content = JsonContent.Create(PricingAllocationTestData.Request(workspace));
            using var denied = await purchase.Http.SendAsync(request); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        }
        await using var foreignDb = app.Database.CreateTenantContext(app.Stores[1].StoreId);
        var foreignLine = await foreignDb.StockDocumentLines.FirstAsync(x => x.StockDocumentId == foreign.ReceiptId);
        var payload = PricingAllocationTestData.Request(workspace); payload.Lines[0].StockDocumentLineId = foreignLine.Id;
        using (var denied = await purchase.Http.PutAsJsonAsync(url, payload)) Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        payload = PricingAllocationTestData.Request(workspace); payload.Lines[0].BillUnitId = foreignLine.UnitId!.Value;
        using (var denied = await purchase.Http.PutAsJsonAsync(url, payload)) Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        await using var localDb = app.Database.CreateTenantContext(app.Stores[0].StoreId);
        Assert.False(await localDb.PurchaseReceiptPricingPlans.AnyAsync());
        Assert.False(await foreignDb.PurchaseReceiptPricingPlans.AnyAsync());
    }
}
