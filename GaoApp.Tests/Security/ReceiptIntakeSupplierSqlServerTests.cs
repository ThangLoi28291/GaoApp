using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class ReceiptIntakeSupplierSqlServerTests
{
    private static string Url(int id) => $"/admin/api/stock-documents/{id}/intake";
    private const string HeaderUrl = "/admin/stock-documents/update-header";
    private static string Version(JsonElement state) => state.GetProperty("documentRowVersion").GetString()!;
    private static object Review(JsonElement state, Seed seed, string? version = null) => new {
        commandId = Guid.NewGuid(), documentRowVersion = version ?? Version(state), approve = true, categoryId = seed.CategoryId,
        itemRowVersion = state.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == seed.ItemIds[0]).GetProperty("rowVersion").GetString()
    };

    [Fact]
    public async Task Manager_saves_supplier_then_reviews_using_new_version_without_posting_stock_or_duplicate_creation()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        var seed = await SeedAsync(app, store);
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store,
            PermissionCodes.Inventory.StockDocument.Approve, PermissionCodes.Catalog.Product.Create));
        var lookup = await manager.JsonAsync(HttpMethod.Get, "/admin/api/suppliers/select2?term=" + Uri.EscapeDataString(seed.SupplierName));
        Assert.Equal(seed.SupplierId.ToString(), Assert.Single(lookup.GetProperty("results").EnumerateArray()).GetProperty("id").GetString());
        var state = (await manager.JsonAsync(HttpMethod.Get, Url(seed.ReceiptId))).GetProperty("state");
        var reviewUrl = Url(seed.ReceiptId) + $"/{seed.ItemIds[0]}/review";
        using (var missing = await manager.Http.PostAsJsonAsync(reviewUrl, Review(state, seed)))
        {
            Assert.Equal(HttpStatusCode.Conflict, missing.StatusCode);
            Assert.Contains("nhà cung cấp", (await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString());
        }
        var saved = await manager.JsonAsync(HttpMethod.Post, HeaderUrl, new {
            stockDocumentId = seed.ReceiptId, rowVersion = Version(state), warehouseId = store.WarehouseId,
            legalEntityId = seed.LegalEntityId, supplierId = seed.SupplierId
        });
        Assert.True(saved.GetProperty("success").GetBoolean());
        Assert.NotEqual(Version(state), saved.GetProperty("rowVersion").GetString());
        using (var stale = await manager.Http.PostAsJsonAsync(reviewUrl, Review(state, seed)))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var review = Review(state, seed, saved.GetProperty("rowVersion").GetString());
        var result = await manager.JsonAsync(HttpMethod.Post, reviewUrl, review);
        var replay = await manager.JsonAsync(HttpMethod.Post, reviewUrl, review);
        Assert.Equal(1, result.GetProperty("unresolvedCount").GetInt32());
        Assert.Equal(1, replay.GetProperty("unresolvedCount").GetInt32());
        await using var check = app.Database.CreateTenantContext(store.StoreId);
        var created = Assert.Single(await check.Products.Where(x => x.Name == seed.ProductNames[0]).ToListAsync());
        Assert.Equal(seed.SupplierId, created.SupplierId);
        Assert.False(created.IsSellable);
        var doc = await check.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId);
        Assert.Equal(seed.SupplierId, doc.SupplierId);
        Assert.Equal(StockDocumentStatus.PendingApproval, doc.Status);
        Assert.Contains(await check.StockDocumentLines.Include(x => x.ProductVariant).Where(x => x.StockDocumentId == doc.Id).ToListAsync(),
            x => x.ProductVariant.ProductId == created.Id && x.BaseQuantity == 3);
        Assert.Equal(100, (await check.InventoryBalances.SingleAsync()).OnHandQty);
    }

    [Fact]
    public async Task Employee_foreign_supplier_and_stale_header_cannot_change_pending_receipt_or_create_products()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        var seed = await SeedAsync(app, store);
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        using var employee = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.Update));
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        var state = (await manager.JsonAsync(HttpMethod.Get, Url(seed.ReceiptId))).GetProperty("state");
        object Header(int supplierId) => new { stockDocumentId = seed.ReceiptId, rowVersion = Version(state),
            warehouseId = store.WarehouseId, legalEntityId = seed.LegalEntityId, supplierId };
        using (var denied = await employee.Http.GetAsync("/admin/api/suppliers/select2?term=")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await employee.Http.PostAsJsonAsync(HeaderUrl, Header(seed.SupplierId))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await employee.Http.PostAsJsonAsync(Url(seed.ReceiptId) + $"/{seed.ItemIds[0]}/review", Review(state, seed))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await foreign.Http.PostAsJsonAsync(HeaderUrl, Header(seed.SupplierId))) Assert.False(denied.IsSuccessStatusCode);
        int foreignSupplier;
        await using (var db = app.Database.CreateTenantContext(app.Stores[1].StoreId)) foreignSupplier = (await db.Suppliers.SingleAsync()).Id;
        using (var invalid = await manager.Http.PostAsJsonAsync(HeaderUrl, Header(foreignSupplier))) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        await manager.JsonAsync(HttpMethod.Post, HeaderUrl, Header(seed.SupplierId));
        using (var stale = await manager.Http.PostAsJsonAsync(HeaderUrl, Header(seed.SupplierId))) Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
        var foreignLookup = await foreign.JsonAsync(HttpMethod.Get, "/admin/api/suppliers/select2?term=" + Uri.EscapeDataString(seed.SupplierName));
        Assert.Empty(foreignLookup.GetProperty("results").EnumerateArray());
        await using var check = app.Database.CreateTenantContext(store.StoreId);
        Assert.False(await check.Products.AnyAsync(x => seed.ProductNames.Contains(x.Name)));
        Assert.Equal(seed.SupplierId, (await check.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId)).SupplierId);
        Assert.Equal(100, (await check.InventoryBalances.SingleAsync()).OnHandQty);
    }

    internal sealed record Seed(int ReceiptId, int SupplierId, string SupplierName, int CategoryId, int? LegalEntityId, int[] ItemIds, string[] ProductNames);
    internal static async Task<Seed> SeedAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store)
    {
        var basic = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, store);
        int supplierId, categoryId, unitId; int? legalEntityId;
        const string supplierName = "Nhà cung cấp duyệt hàng thử";
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var doc = await db.StockDocuments.SingleAsync(x => x.Id == basic.ReceiptId); doc.SupplierId = null;
            var supplier = await db.Suppliers.SingleAsync(); supplierId = supplier.Id; supplier.Name = supplierName;
            var product = (await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == store.VariantId)).Product;
            categoryId = product.CategoryId; unitId = product.BaseUnitId;
            legalEntityId = (await db.Warehouses.SingleAsync(x => x.Id == store.WarehouseId)).LegalEntityId;
            await db.SaveChangesAsync();
        }
        using var employee = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.Update));
        var state = (await employee.JsonAsync(HttpMethod.Get, Url(basic.ReceiptId))).GetProperty("state");
        string[] names = ["Bánh tráng duyệt nhà cung cấp", "Hàng mới dùng nhà cung cấp đã lưu"];
        foreach (var name in names)
            state = await employee.JsonAsync(HttpMethod.Post, Url(basic.ReceiptId), new { commandId = Guid.NewGuid(),
                documentRowVersion = Version(state), name, baseUnitId = unitId, unitId, factor = 1, quantity = 3, categoryId });
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var doc = await db.StockDocuments.SingleAsync(x => x.Id == basic.ReceiptId);
            doc.Status = StockDocumentStatus.PendingApproval; doc.SubmittedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        var itemIds = names.Select(name => state.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("name").GetString() == name).GetProperty("id").GetInt32()).ToArray();
        return new(basic.ReceiptId, supplierId, supplierName, categoryId, legalEntityId, itemIds, names);
    }
}
