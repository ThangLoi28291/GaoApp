using System.Net;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class StoreMonitor3DHttpTests
{
    [Fact]
    public async Task Real_current_cart_and_online_roster_are_tenant_scoped_allowlisted_and_permission_protected()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var admin = await db.Roles.SingleAsync(x => x.Code == "ADMIN");
            var permission = await db.Permissions.SingleAsync(x => x.Code == PermissionCodes.Admin.StoreMonitorView);
            Assert.False(await db.RolePermissions.AnyAsync(x => x.RoleId == admin.Id && x.PermissionId == permission.Id));
        }
        var workerAccount = await app.AddAccountAsync(store, "*");
        using var worker = await app.LoginAsync(workerAccount);
        using var monitor = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Admin.StoreMonitorView));
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], PermissionCodes.Admin.StoreMonitorView));
        using var denied = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Admin.DashboardView));
        foreach (var url in new[] { "/admin/store-monitor", "/admin/api/store-activity/snapshot", "/admin/api/store-activity/stream" })
        {
            using var response = await denied.Http.GetAsync(url);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        using var pulse = await denied.Http.PostAsync("/admin/api/store-activity/online", new FormUrlEncodedContent(
            new Dictionary<string,string> { ["tabId"] = Guid.NewGuid().ToString(), ["userId"] = workerAccount.UserId.ToString() }));
        Assert.Equal(HttpStatusCode.NoContent, pulse.StatusCode); // No view permission needed to appear at work.
        var online = await monitor.JsonAsync(HttpMethod.Get, "/admin/api/store-activity/snapshot");
        Assert.Contains(online.GetProperty("employees").EnumerateArray(), x => x.GetProperty("userId").GetInt32() == workerAccount.UserId);
        Assert.DoesNotContain((await foreign.JsonAsync(HttpMethod.Get, "/admin/api/store-activity/snapshot"))
            .GetProperty("employees").EnumerateArray(), x => x.GetProperty("userId").GetInt32() == workerAccount.UserId);
        await worker.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash=0, warehouseId=store.WarehouseId });
        var draft = await worker.JsonAsync(HttpMethod.Post, "/admin/pos/draft");
        var order = draft.GetProperty("orderId").GetInt32();
        var barcode="MONITOR-SCAN-"+Guid.NewGuid().ToString("N");
        await using (var db=app.Database.CreateTenantContext(store.StoreId))
        {
            var variant=await db.ProductVariants.Include(x=>x.Product).SingleAsync(x=>x.Id==store.VariantId);
            var conversion=new ProductUnitConversion { StoreId=store.StoreId,ProductVariantId=variant.Id,
                UnitId=variant.Product.BaseUnitId,Factor=1,IsBaseUnit=true,IsDefaultForSale=true,Price=variant.Price };
            conversion.Barcodes.Add(new() { StoreId=store.StoreId,Barcode=barcode });
            db.ProductUnitConversions.Add(conversion);await db.SaveChangesAsync();
        }
        await worker.JsonAsync(HttpMethod.Post, $"/admin/pos/{order}/items?variantId={store.VariantId}&qty=2");
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var customer = new Customer { StoreId=store.StoreId, Name="Khách tích điểm thật", Phone="PRIVATE PHONE", Address="PRIVATE ADDRESS", Note="PRIVATE NOTE" };
            db.Customers.Add(customer); await db.SaveChangesAsync();
            var saved = await db.Orders.SingleAsync(x => x.Id == order); saved.CustomerId=customer.Id; await db.SaveChangesAsync();
        }
        // The shared reader intentionally expires on its next one-second refresh.
        await Task.Delay(1100);
        var snapshot = await monitor.JsonAsync(HttpMethod.Get, "/admin/api/store-activity/snapshot");
        var cart = Assert.Single(snapshot.GetProperty("checkouts").EnumerateArray());
        Assert.Equal(order,cart.GetProperty("orderId").GetInt32());
        Assert.Equal("Khách tích điểm thật",cart.GetProperty("customerName").GetString());
        Assert.Equal(40m,cart.GetProperty("grandTotal").GetDecimal());
        Assert.Equal(2m,Assert.Single(cart.GetProperty("lines").EnumerateArray()).GetProperty("quantity").GetDecimal());
        var firstScan = snapshot.GetProperty("events").EnumerateArray().First(e => e.GetProperty("action").GetString() == "POS.AddItem").GetProperty("posScan");
        Assert.Equal(2m, firstScan.GetProperty("addedQuantity").GetDecimal());
        Assert.Equal(2m, firstScan.GetProperty("quantity").GetDecimal());
        Assert.Equal(20m, firstScan.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(40m, firstScan.GetProperty("lineTotal").GetDecimal());
        Assert.Equal(Assert.Single(cart.GetProperty("lines").EnumerateArray()).GetProperty("lineId").GetInt32(),firstScan.GetProperty("lineId").GetInt32());
        Assert.DoesNotContain("PRIVATE",snapshot.ToString());
        Assert.DoesNotContain("customerPhone",snapshot.ToString(),StringComparison.OrdinalIgnoreCase);
        var foreignCart = Assert.Single((await foreign.JsonAsync(HttpMethod.Get, "/admin/api/store-activity/snapshot")).GetProperty("checkouts").EnumerateArray());
        Assert.Equal(System.Text.Json.JsonValueKind.Null,foreignCart.GetProperty("orderId").ValueKind);
        // All three successful entry paths publish saved deltas. POS normalizes qty=0 to one.
        foreach(var (url,body,action,added,quantity) in new (string,object?,string,decimal,decimal)[] {
            ($"/admin/pos/{order}/items?variantId={store.VariantId}&qty=0",null,"POS.AddItem",1m,3m),
            ($"/admin/pos/{order}/barcode?barcode={barcode}&qty=2",null,"POS.AddByBarcode",2m,5m),
            ("/admin/pos/cart/current/scan",new { barcode,quantity=1m },"POS.ScanToCurrentCart",1m,6m) })
        {
            await worker.JsonAsync(HttpMethod.Post,url,body);
            var updated=await monitor.JsonAsync(HttpMethod.Get,"/admin/api/store-activity/snapshot");
            var scan=updated.GetProperty("events").EnumerateArray().First(e=>e.GetProperty("action").GetString()==action).GetProperty("posScan");
            Assert.Equal(added,scan.GetProperty("addedQuantity").GetDecimal());
            Assert.Equal(quantity,scan.GetProperty("quantity").GetDecimal());
            Assert.DoesNotContain("PRIVATE",scan.ToString());
        }
        var beforeFailed=await monitor.JsonAsync(HttpMethod.Get,"/admin/api/store-activity/snapshot");
        using(var failed=await worker.Http.PostAsync($"/admin/pos/{order}/barcode?barcode=DOES-NOT-EXIST",null))
            Assert.False(failed.IsSuccessStatusCode);
        var afterFailed=await monitor.JsonAsync(HttpMethod.Get,"/admin/api/store-activity/snapshot");
        Assert.Equal(beforeFailed.GetProperty("events")[0].GetProperty("id").GetInt64(),afterFailed.GetProperty("events")[0].GetProperty("id").GetInt64());
        using var logout = await worker.Http.PostAsync("/admin/account/logout",null);
        Assert.Equal(HttpStatusCode.Redirect,logout.StatusCode);
        Assert.DoesNotContain((await monitor.JsonAsync(HttpMethod.Get, "/admin/api/store-activity/snapshot")).GetProperty("employees").EnumerateArray(),
            x => x.GetProperty("userId").GetInt32()==workerAccount.UserId);
    }
}
