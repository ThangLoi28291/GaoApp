using System.Net;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Services.Inventory;
using GaoApp.Tests.Inventory;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class InventoryLedgerInboundCostSqlServerTests
{
    [Fact]
    public async Task Admin_sees_original_receipt_cost_even_after_consumption_without_exposing_cost_to_other_users()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var admin = await app.AddAccountAsync(store, PermissionCodes.Inventory.Transaction.View);
        var staff = await app.AddAccountAsync(store, "*");
        int adminRoleId;
        await using (var db = app.Database.CreateHostContext())
        {
            adminRoleId = await db.Roles.Where(x => x.StoreId == store.StoreId && x.Code == "ADMIN").Select(x => x.Id).SingleAsync();
            (await db.UserInStores.SingleAsync(x => x.StoreId == store.StoreId && x.UserId == admin.UserId)).RoleId = adminRoleId;
            (await db.ProductVariants.SingleAsync(x => x.Id == store.VariantId)).CostPrice = 999;
            await db.SaveChangesAsync();
        }

        async Task<int> ReceiveAsync(decimal quantity, decimal price, string reference)
        {
            await using var db = app.Database.CreateTenantContext(store.StoreId);
            await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(
                new InventoryMovementFactory().CreatePurchaseReceipt(store.WarehouseId, store.VariantId,
                    quantity, price, reference, 1, reference, 1));
            return await db.InventoryTransactions.Where(x => x.ReferenceId == reference).Select(x => x.Id).SingleAsync();
        }
        async Task<int> IssueAsync(decimal quantity)
        {
            await using var db = app.Database.CreateTenantContext(store.StoreId);
            var request = new InventoryMovementFactory().CreateAdjustmentDecrease(
                store.WarehouseId, store.VariantId, quantity, 6000, "Ledger inbound cost test");
            request.AllowNegativeBalance = true;
            await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(request);
            return await db.InventoryTransactions.OrderByDescending(x => x.Id).Select(x => x.Id).FirstAsync();
        }

        var originalId = await ReceiveAsync(240, 4583.3333m, "LEDGER-RECEIPT-01");
        var newerId = await ReceiveAsync(20, 6000, "LEDGER-RECEIPT-02");
        // Exhaust the initial 100 @ 10 and the entire 240-unit receipt.
        var issueId = await IssueAsync(340);
        await IssueAsync(21);
        var replenishId = await ReceiveAsync(2, 9000, "LEDGER-REPLENISH");
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            Assert.Equal(0m, (await db.InventoryCostLayers.SingleAsync(x => x.InventoryTransactionId == originalId)).RemainingQuantity);
            // Replenishment creates a revaluation for earlier provisional issues.
            Assert.NotEqual(18000m, (await db.InventoryTransactions.SingleAsync(x => x.Id == replenishId)).TotalCost);
        }

        using var adminClient = await app.LoginAsync(admin);
        using var staffClient = await app.LoginAsync(staff);
        var pagePath = $"/admin/inventory-ledger/data?warehouseId={store.WarehouseId}";
        var quickPath = $"/admin/inventory-ledger/quick-view?transactionId={originalId}";
        var beforeRead = await SnapshotAsync(app, store);
        var page = await adminClient.JsonAsync(HttpMethod.Get, pagePath);
        Assert.True(page.GetProperty("canViewCost").GetBoolean());
        var items = page.GetProperty("items").EnumerateArray().ToArray();
        JsonElement CostOf(int id) => items.Single(x => x.GetProperty("transactionId").GetInt32() == id).GetProperty("inboundCost");
        var original = CostOf(originalId);
        Assert.Equal(4583.3333m, original.GetProperty("unitCost").GetDecimal());
        Assert.Equal(1099999.992m, original.GetProperty("totalCost").GetDecimal());
        Assert.False(original.GetProperty("isProvisional").GetBoolean());
        Assert.False(original.GetProperty("isMixedCost").GetBoolean());
        Assert.Equal(6000m, CostOf(newerId).GetProperty("unitCost").GetDecimal());
        Assert.Equal(9000m, CostOf(replenishId).GetProperty("unitCost").GetDecimal());
        Assert.Equal(18000m, CostOf(replenishId).GetProperty("totalCost").GetDecimal());
        AssertNoCost(items.Single(x => x.GetProperty("transactionId").GetInt32() == issueId));
        var quick = await adminClient.JsonAsync(HttpMethod.Get, quickPath);
        Assert.Equal(original.GetRawText(), quick.GetProperty("item").GetProperty("inboundCost").GetRawText());
        AssertNoCost((await adminClient.JsonAsync(HttpMethod.Get,
            $"/admin/inventory-ledger/quick-view?transactionId={issueId}")).GetProperty("item"));
        using (var response = await adminClient.Http.GetAsync(pagePath))
            Assert.True(response.Headers.CacheControl?.NoStore);
        using (var response = await adminClient.Http.GetAsync(quickPath))
            Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(beforeRead, await SnapshotAsync(app, store));

        // Granting all ordinary permissions and forging cost flags must not bypass ADMIN.
        var staffPage = await staffClient.JsonAsync(HttpMethod.Get,
            pagePath + $"&costViewerUserId={admin.UserId}&includeCost=true&canViewCost=true");
        Assert.False(staffPage.GetProperty("canViewCost").GetBoolean());
        Assert.All(staffPage.GetProperty("items").EnumerateArray(), AssertNoCost);
        var staffQuick = await staffClient.JsonAsync(HttpMethod.Get, quickPath);
        AssertNoCost(staffQuick.GetProperty("item"));
        var crossPage = await adminClient.JsonAsync(HttpMethod.Get,
            $"/admin/inventory-ledger/data?warehouseId={app.Stores[1].WarehouseId}");
        Assert.Empty(crossPage.GetProperty("items").EnumerateArray());
        int otherTransactionId;
        await using (var db = app.Database.CreateHostContext())
            otherTransactionId = await db.InventoryTransactions.Where(x => x.StoreId == app.Stores[1].StoreId).Select(x => x.Id).FirstAsync();
        using (var response = await adminClient.Http.GetAsync($"/admin/inventory-ledger/quick-view?transactionId={otherTransactionId}"))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var evidence = Path.Combine(FullApplicationFixture.SourceRoot(), "Logs", "inventory-ledger-cost-ui");
        Directory.CreateDirectory(evidence);
        foreach (var (profile, client, list, detail) in new[]
        {
            ("admin", adminClient, page, quick), ("staff", staffClient, staffPage, staffQuick)
        })
        {
            await File.WriteAllTextAsync(Path.Combine(evidence, $"{profile}.html"), await client.Http.GetStringAsync("/admin/inventory-ledger"));
            await File.WriteAllTextAsync(Path.Combine(evidence, $"{profile}-page.json"), list.GetRawText());
            await File.WriteAllTextAsync(Path.Combine(evidence, $"{profile}-quick.json"), detail.GetRawText());
        }

        await using (var db = app.Database.CreateHostContext())
        {
            (await db.Roles.SingleAsync(x => x.Id == adminRoleId)).IsSystemRole = false;
            await db.SaveChangesAsync();
        }
        using (var response = await adminClient.Http.GetAsync(pagePath))
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var refreshed = await app.LoginAsync(admin);
        var revoked = await refreshed.JsonAsync(HttpMethod.Get, pagePath);
        Assert.False(revoked.GetProperty("canViewCost").GetBoolean());
        Assert.All(revoked.GetProperty("items").EnumerateArray(), AssertNoCost);
        AssertNoCost((await refreshed.JsonAsync(HttpMethod.Get, quickPath)).GetProperty("item"));
    }

    private static void AssertNoCost(JsonElement item)
    {
        Assert.False(item.TryGetProperty("inboundCost", out _));
        Assert.DoesNotContain("unitCost", item.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("totalCost", item.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> SnapshotAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var balance = await db.InventoryBalances.AsNoTracking().SingleAsync(x => x.WarehouseId == store.WarehouseId && x.ProductVariantId == store.VariantId);
        var layers = await db.InventoryCostLayers.OrderBy(x => x.Id).Select(x => new { x.UnitCost, x.RemainingQuantity }).ToArrayAsync();
        var entries = await db.InventoryValuationEntries.OrderBy(x => x.Id).Select(x => new { x.Quantity, x.UnitCost, x.Amount }).ToArrayAsync();
        return JsonSerializer.Serialize(new { balance.OnHandQty, balance.InventoryValue, balance.AverageUnitCost, layers, entries });
    }
}
