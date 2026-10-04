using System.Net;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class ReceiptRetailComparisonSqlServerTests
{
    [Fact]
    public async Task Approval_displays_current_sku_unit_retail_prices_with_existing_fallbacks_and_no_writes()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var seed = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, store);
        await using var baseline = app.Database.CreateTenantContext(store.StoreId);
        var beforeStock = await baseline.InventoryTransactions.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.QuantityChange, x.TotalCost, x.AfterQty, x.AfterInventoryValue }).ToListAsync();
        byte[] receiptVersion;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var doc = await db.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId);
            doc.Status = StockDocumentStatus.PendingApproval; await db.SaveChangesAsync(); receiptVersion = doc.RowVersion;
        }
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.Approve));
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        var url = $"/admin/stock-documents/{seed.ReceiptId}";
        async Task<string> Html() => WebUtility.HtmlDecode(await manager.Http.GetStringAsync(url));
        var html = await Html();
        Assert.Contains("Giá bán lẻ hiện tại", html); Assert.Contains("48.000 đ/Lốc", html); Assert.Contains("280.000 đ/Thùng", html);
        using (var denied = await foreign.Http.GetAsync(url)) Assert.NotEqual(HttpStatusCode.OK, denied.StatusCode);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            Assert.Equal(receiptVersion, (await db.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId)).RowVersion);
            var pack = await db.ProductUnitConversions.SingleAsync(x => x.Id == seed.PackId); pack.Price = 0;
            var variant = await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == store.VariantId);
            variant.Price = 13500; variant.Product.BasePrice = 12500; await db.SaveChangesAsync();
        }
        Assert.Contains("13.500 đ/Lốc", await Html());
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            (await db.ProductVariants.SingleAsync(x => x.Id == store.VariantId)).Price = 0; await db.SaveChangesAsync();
        }
        Assert.Contains("12.500 đ/Lốc", await Html());
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            (await db.ProductUnitConversions.SingleAsync(x => x.Id == seed.PackId)).IsActive = false; await db.SaveChangesAsync();
        }
        html = await Html(); Assert.Contains("12.500 đ/Hộp", html); Assert.DoesNotContain("12.500 đ/Lốc", html);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var variant = await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == store.VariantId);
            variant.Product.BasePrice = 0; await db.SaveChangesAsync();
        }
        Assert.Contains("Chưa có giá bán", await Html());
        await using var check = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(receiptVersion, (await check.StockDocuments.SingleAsync(x => x.Id == seed.ReceiptId)).RowVersion);
        Assert.Equal(beforeStock, await check.InventoryTransactions.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.QuantityChange, x.TotalCost, x.AfterQty, x.AfterInventoryValue }).ToListAsync());
    }
}
