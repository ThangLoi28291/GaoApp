using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

// Used only by the standalone browser probe against FullApplicationFixture's disposable GUID database.
internal static class PosEntryBrowserFixture
{
    internal sealed record Seed(int OrderId, FullApplicationFixture.Account Denied, FullApplicationFixture.Account Closed);
    internal static async Task<Seed> PrepareAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store,
        FullApplicationFixture.Account account)
    {
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            (await db.POSTerminals.SingleAsync(x => x.Id == store.TerminalId)).Name = "Quầy bán hàng số 01 — khu vực khách mua mang đi";
            (await db.Warehouses.SingleAsync(x => x.Id == store.WarehouseId)).Name = "Kho bán lẻ trung tâm — hàng tiêu dùng và thực phẩm";
            await db.SaveChangesAsync();
        }
        using var client = await app.LoginAsync(account);
        var draft = await client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/new", new { });
        var orderId = draft.GetProperty("orderId").GetInt32();
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var water = await db.ProductVariants.SingleAsync(x => x.Sku == "WATER-TEST");
            await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/items?variantId={water.Id}&qty=2");
        }
        return new(orderId, await app.AddAccountAsync(store, PermissionCodes.Admin.DashboardView),
            await app.AddAccountAsync(app.Stores[1], "*"));
    }

    internal static async Task<string> FingerprintAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        return JsonSerializer.Serialize(new {
            menus = await db.AdminMenuItems.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.StoreId, x.ParentId, x.Title, x.Url, x.Controller, x.Action, x.PermissionCode, x.SortOrder, x.IsActive, x.RowVersion }).ToListAsync(),
            stock = await db.InventoryBalances.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.StoreId, x.WarehouseId, x.ProductVariantId, x.OnHandQty, x.RowVersion }).ToListAsync(),
            payments = await db.OrderPayments.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.OrderId, x.Amount, x.RowVersion }).ToListAsync(),
            catalog = await db.ProductUnitConversions.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.ProductVariantId, x.Factor, x.Price, x.RowVersion }).ToListAsync(),
            otherOrders = await db.Orders.IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId != store.StoreId).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.Status, x.RowVersion }).ToListAsync(),
            movements = await db.InventoryTransactions.IgnoreQueryFilters().CountAsync(),
            shifts = await db.POSShifts.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.Status, x.RowVersion }).ToListAsync()
        });
    }

    internal static async Task<object> VerifyAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, int orderId)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var orders = await db.Orders.AsNoTracking().ToListAsync();
        if (orders.Count != 1 || orders[0].Id != orderId || orders[0].Status != OrderStatus.Draft)
            throw new InvalidOperationException("The same seeded draft must survive all phone/desktop transitions.");
        var lines = await db.OrderLines.AsNoTracking().Where(x => x.OrderId == orderId && !x.IsDeleted).ToListAsync();
        var beerId = (await db.ProductVariants.SingleAsync(x => x.ProductVariantName == "Bia Ken bạc 330 ml")).Id;
        var waterId = (await db.ProductVariants.SingleAsync(x => x.Sku == "WATER-TEST")).Id;
        if (lines.Count != 3 || lines.Single(x => x.VariantId == beerId && x.SellingUnitName == "Lon").Quantity != 5
            || lines.Single(x => x.VariantId == beerId && x.SellingUnitName == "Lốc").Quantity != 1
            || lines.Single(x => x.VariantId == waterId).Quantity != 3)
            throw new InvalidOperationException("Search/touch/pack/scanner must each add exactly the intended quantity.");
        return new { passed = true, sameDraft = true, orderId, lineCount = lines.Count,
            lines = lines.Select(x => new { x.VariantId, x.SellingUnitName, x.Quantity, x.BaseQuantity }),
            storeMenusStockPaymentsCatalogOtherStoreAndShiftUnchanged = true };
    }
}
