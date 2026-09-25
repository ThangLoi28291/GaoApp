using System.Net;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Tests.Inventory;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class InventoryInquiryCostSqlServerTests
{
    [Fact]
    public async Task Costs_follow_fifo_and_are_only_returned_to_current_store_system_admin()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var otherStore = app.Stores[1];
        var admin = await app.AddAccountAsync(store, PermissionCodes.Inventory.Balance.View);
        var nonAdmin = await app.AddAccountAsync(store, "*");
        int adminRoleId;
        await using (var db = app.Database.CreateHostContext())
        {
            var role = await db.Roles.SingleAsync(x => x.StoreId == store.StoreId && x.Code == "ADMIN");
            adminRoleId = role.Id;
            (await db.UserInStores.SingleAsync(x => x.UserId == admin.UserId && x.StoreId == store.StoreId)).RoleId = role.Id;
            (await db.ProductVariants.SingleAsync(x => x.Id == store.VariantId)).CostPrice = 999;
            await db.SaveChangesAsync();
        }
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(
                new InventoryMovementFactory().CreatePurchaseReceipt(
                    store.WarehouseId, store.VariantId, 20, 20, "INQUIRY-COST", 1, "SECOND-RECEIPT", 1));
        }

        using var adminClient = await app.LoginAsync(admin);
        using var nonAdminClient = await app.LoginAsync(nonAdmin);
        var path = $"/admin/inventory-inquiry/data?warehouseId={store.WarehouseId}";
        var quickPath = $"/admin/inventory-inquiry/quick-view?warehouseId={store.WarehouseId}&productVariantId={store.VariantId}";
        var beforeRead = await SnapshotAsync(app, store);
        var page = await adminClient.JsonAsync(HttpMethod.Get, path);
        Assert.True(page.GetProperty("canViewCost").GetBoolean());
        var item = Assert.Single(page.GetProperty("items").EnumerateArray());
        var cost = item.GetProperty("cost");
        Assert.Equal(10m, cost.GetProperty("nextFifoUnitCost").GetDecimal());
        Assert.Equal(100m, cost.GetProperty("nextFifoRemainingQuantity").GetDecimal());
        Assert.Equal(20m, cost.GetProperty("lastInboundUnitCost").GetDecimal());
        Assert.Equal(1400m, cost.GetProperty("inventoryValue").GetDecimal());
        Assert.InRange(cost.GetProperty("averageUnitCost").GetDecimal(), 11.6666m, 11.6668m);
        Assert.False(cost.GetProperty("hasProvisionalCost").GetBoolean());
        var quick = await adminClient.JsonAsync(HttpMethod.Get, quickPath);
        Assert.Equal(cost.GetRawText(), quick.GetProperty("cost").GetRawText());
        using (var response = await adminClient.Http.GetAsync(path))
            Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(beforeRead, await SnapshotAsync(app, store));

        // Extra permissions and caller-controlled query flags must not reveal costs.
        var deniedPage = await nonAdminClient.JsonAsync(HttpMethod.Get,
            path + $"&costViewerUserId={admin.UserId}&canViewCost=true&includeCost=true");
        Assert.False(deniedPage.GetProperty("canViewCost").GetBoolean());
        AssertNoCost(Assert.Single(deniedPage.GetProperty("items").EnumerateArray()));
        var deniedQuick = await nonAdminClient.JsonAsync(HttpMethod.Get, quickPath);
        AssertNoCost(deniedQuick);
        var crossStore = await adminClient.JsonAsync(HttpMethod.Get,
            $"/admin/inventory-inquiry/data?warehouseId={otherStore.WarehouseId}");
        Assert.Empty(crossStore.GetProperty("items").EnumerateArray());
        using (var crossQuick = await adminClient.Http.GetAsync(
            $"/admin/inventory-inquiry/quick-view?warehouseId={otherStore.WarehouseId}&productVariantId={otherStore.VariantId}"))
            Assert.Equal(HttpStatusCode.NotFound, crossQuick.StatusCode);

        // Save synthetic, real API/page output for optional browser layout verification.
        var evidence = Path.Combine(FullApplicationFixture.SourceRoot(), "Logs", "inventory-admin-cost-ui");
        Directory.CreateDirectory(evidence);
        await File.WriteAllTextAsync(Path.Combine(evidence, "admin.html"), await adminClient.Http.GetStringAsync("/admin/inventory-inquiry"));
        await File.WriteAllTextAsync(Path.Combine(evidence, "admin-page.json"), page.GetRawText());
        await File.WriteAllTextAsync(Path.Combine(evidence, "admin-quick.json"), quick.GetRawText());
        await File.WriteAllTextAsync(Path.Combine(evidence, "staff.html"), await nonAdminClient.Http.GetStringAsync("/admin/inventory-inquiry"));
        await File.WriteAllTextAsync(Path.Combine(evidence, "staff-page.json"), deniedPage.GetRawText());
        await File.WriteAllTextAsync(Path.Combine(evidence, "staff-quick.json"), deniedQuick.GetRawText());

        // Exhaust the oldest layer; the displayed FIFO cost must move to the next lot.
        await DecreaseAsync(app, store, 100);
        var afterFirstLot = (await adminClient.JsonAsync(HttpMethod.Get, quickPath)).GetProperty("cost");
        Assert.Equal(20m, afterFirstLot.GetProperty("nextFifoUnitCost").GetDecimal());
        Assert.Equal(20m, afterFirstLot.GetProperty("averageUnitCost").GetDecimal());
        Assert.Equal(400m, afterFirstLot.GetProperty("inventoryValue").GetDecimal());
        await DecreaseAsync(app, store, 20);
        var empty = (await adminClient.JsonAsync(HttpMethod.Get, quickPath)).GetProperty("cost");
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("nextFifoUnitCost").ValueKind);
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("averageUnitCost").ValueKind);
        Assert.Equal(0m, empty.GetProperty("inventoryValue").GetDecimal());
        await DecreaseAsync(app, store, 1);
        var negative = await adminClient.JsonAsync(HttpMethod.Get, quickPath);
        Assert.True(negative.GetProperty("cost").GetProperty("hasProvisionalCost").GetBoolean());
        Assert.Equal(JsonValueKind.Null, negative.GetProperty("cost").GetProperty("nextFifoUnitCost").ValueKind);
        await File.WriteAllTextAsync(Path.Combine(evidence, "negative-quick.json"), negative.GetRawText());

        // Recheck the database role on every read; an ADMIN claim alone is insufficient.
        await using (var db = app.Database.CreateHostContext())
        {
            (await db.Roles.SingleAsync(x => x.Id == adminRoleId)).IsSystemRole = false;
            await db.SaveChangesAsync();
        }
        using (var expired = await adminClient.Http.GetAsync(path))
            Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        using var refreshed = await app.LoginAsync(admin);
        var revoked = await refreshed.JsonAsync(HttpMethod.Get, path);
        Assert.False(revoked.GetProperty("canViewCost").GetBoolean());
        AssertNoCost(Assert.Single(revoked.GetProperty("items").EnumerateArray()));
        AssertNoCost(await refreshed.JsonAsync(HttpMethod.Get, quickPath));
    }

    private static void AssertNoCost(JsonElement item)
    {
        Assert.False(item.TryGetProperty("cost", out _));
        Assert.DoesNotContain("unitCost", item.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("inventoryValue", item.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> SnapshotAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var balance = await db.InventoryBalances.AsNoTracking().SingleAsync(x => x.WarehouseId == store.WarehouseId && x.ProductVariantId == store.VariantId);
        var quantities = await db.InventoryCostLayers.OrderBy(x => x.Id).Select(x => x.RemainingQuantity).ToArrayAsync();
        return JsonSerializer.Serialize(new { balance.OnHandQty, balance.InventoryValue, balance.AverageUnitCost, quantities });
    }

    private static async Task DecreaseAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, decimal quantity)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var request = new InventoryMovementFactory().CreateAdjustmentDecrease(
            store.WarehouseId, store.VariantId, quantity, 20, "Inventory inquiry cost test");
        request.AllowNegativeBalance = true;
        await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(request);
    }
}
