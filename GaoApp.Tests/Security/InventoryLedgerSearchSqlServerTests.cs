using System.Diagnostics;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Inventory;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class InventoryLedgerSearchSqlServerTests(ITestOutputHelper output)
{
    internal static async Task<int> SeedAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, int backgroundCount)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var source = await db.Products.SingleAsync();
        var milk = new Product { StoreId = store.StoreId, CategoryId = source.CategoryId,
            BaseUnitId = source.BaseUnitId, SupplierId = source.SupplierId, Name = "Sữa tươi Đà Lạt", Alias = "milk-ledger",
            IsActive = false, Variants = [
                new ProductVariant { StoreId = store.StoreId, Sku = "MILK-LEDGER", IsActive = false },
                new ProductVariant { StoreId = store.StoreId, Sku = "MILK-NO-MOVEMENT", IsActive = false }] };
        db.Add(milk); await db.SaveChangesAsync();
        var milkId = milk.Variants.Single(v => v.Sku == "MILK-LEDGER").Id;
        db.Add(new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = milkId, UnitId = source.BaseUnitId,
            IsBaseUnit = true, Barcodes = [new ProductVariantUnitBarcode { StoreId = store.StoreId, Barcode = "8938505974194" }] });
        for (int i = 0; i < 45; i++) db.Add(new InventoryTransaction { StoreId = store.StoreId, WarehouseId = store.WarehouseId,
            ProductVariantId = milkId, QuantityChange = i % 2 == 0 ? 1 : -1, BeforeQty = 0, AfterQty = i % 2 == 0 ? 1 : -1,
            TransactionType = InventoryTransactionType.PurchaseReceipt, ReferenceType = InventoryReferenceType.StockDocument,
            ReferenceId = "MILK-DOC-" + i, Note = i == 0 ? "Sữa tươi trùng cả ghi chú" : "Nhận hàng",
            OccurredAtUtc = new DateTime(2026, 9, 30).AddMinutes(i) });
        db.Add(new InventoryTransaction { StoreId = store.StoreId, WarehouseId = store.WarehouseId,
            ProductVariantId = store.VariantId, QuantityChange = 2, BeforeQty = 0, AfterQty = 2,
            TransactionType = InventoryTransactionType.AdjustmentIncrease, ReferenceType = InventoryReferenceType.Adjustment,
            ReferenceId = "NOTE-ONLY", Note = "Sữa tươi đối soát", OccurredAtUtc = new DateTime(2026, 9, 29) });
        await db.SaveChangesAsync();
        if (backgroundCount > 0)
        {
            db.Database.SetCommandTimeout(180);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO InventoryTransactions (StoreId, WarehouseId, ProductVariantId, TransactionType, ReferenceType,
                    ReferenceId, QuantityChange, BeforeQty, AfterQty, CostSourceType, OccurredAtUtc, CreatedAtUtc, IsDeleted, Note)
                SELECT TOP ({backgroundCount}) {store.StoreId}, {store.WarehouseId}, {store.VariantId}, 20, 1,
                    CONCAT('BG-', ROW_NUMBER() OVER (ORDER BY (SELECT NULL))), -1, 10, 9, 0,
                    DATEADD(second, -CONVERT(int, ROW_NUMBER() OVER (ORDER BY (SELECT NULL))), '2026-09-28'),
                    SYSUTCDATETIME(), 0, N'Xuất bán hàng tại quầy thu ngân'
                FROM sys.all_objects a CROSS JOIN sys.all_objects b;
                """);
        }
        return milkId;
    }

    [Fact]
    public async Task Search_large_ledger_preserves_accent_alias_note_union_counts_paging_filters_and_tenant()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var initialCount = await db.InventoryTransactions.CountAsync();
        await SeedAsync(app, store, 1_500_000);
        var repo = new InventoryLedgerIndexReadRepository(db);
        async Task<InventoryLedgerIndexPageDto> Search(string keyword, bool summary = true, int page = 1, string? scope = null)
        {
            var clock = Stopwatch.StartNew();
            var result = await repo.QueryAsync(store.StoreId, new() { Keyword = keyword, SearchScope = scope, IncludeSummary = summary, Page = page, PageSize = 20 }, null);
            output.WriteLine($"Search '{keyword}', scope={scope ?? "all"}, summary={summary}: {clock.ElapsedMilliseconds} ms, {result.TotalItems} matches");
            return result;
        }
        Assert.Equal(initialCount + 1_500_046, (await Search("", scope: "product")).TotalItems);
        var productOnly = await Search("sua tuoi", scope: "product");
        Assert.Equal(45, productOnly.TotalItems);
        Assert.Equal(23, productOnly.Summary.IncreaseItems);
        Assert.Equal(45, (await Search("SỮA TƯƠI", scope: "product")).TotalItems);
        Assert.Equal(45, (await Search("da lat", scope: "product")).TotalItems);
        Assert.Equal(45, (await Search("8938505974194", scope: "product")).TotalItems);
        var productLastPage = await Search("sua tuoi", false, 3, "product");
        Assert.Equal(5, productLastPage.Items.Count);
        Assert.Empty(productOnly.Items.Select(x => x.TransactionId).Intersect(productLastPage.Items.Select(x => x.TransactionId)));
        Assert.Empty((await Search("no-such-unique-ledger-value", scope: "product")).Items);
        Assert.Equal(1, (await Search("NOTE-ONLY", scope: "reference")).TotalItems);
        Assert.Equal(1, (await Search("doi soat", scope: "note")).TotalItems);
        var milk = await Search("sua tuoi");
        Assert.Equal(46, milk.TotalItems); Assert.Equal(24, milk.Summary.IncreaseItems); Assert.Equal(22, milk.Summary.DecreaseItems);
        Assert.Equal(22, milk.Summary.NegativeItems); Assert.Equal(20, milk.Items.Count);
        Assert.Equal(46, (await Search("SỮA TƯƠI")).TotalItems);
        Assert.Equal(45, (await Search("da lat")).TotalItems);
        Assert.Equal(45, (await Search("MILK-LEDGER")).TotalItems);
        Assert.Equal(45, (await Search("8938505974194")).TotalItems);
        Assert.Equal(1, (await Search("doi soat")).TotalItems);
        Assert.Equal(1, (await Search("NOTE-ONLY")).TotalItems);
        var second = await Search("sua tuoi", false, 2);
        Assert.Equal(46, second.TotalItems); Assert.Empty(milk.Items.Select(x => x.TransactionId).Intersect(second.Items.Select(x => x.TransactionId)));
        Assert.Equal(6, (await Search("sua tuoi", false, 3)).Items.Count);
        Assert.Empty((await Search("no-such-unique-ledger-value")).Items);
        var negative = await repo.QueryAsync(store.StoreId, new() { Keyword = "sua tuoi", State = "negative", PageSize = 100 }, null);
        Assert.Equal(22, negative.TotalItems); Assert.All(negative.Items, x => Assert.True(x.AfterQty < 0));
        var dated = await repo.QueryAsync(store.StoreId, new() { Keyword = "sua tuoi", FromDate = new(2026, 9, 30), PageSize = 100 }, null);
        Assert.Equal(45, dated.TotalItems);
        Assert.Empty((await repo.QueryAsync(store.StoreId, new() { Keyword = "sua tuoi", WarehouseId = app.Stores[1].WarehouseId }, null)).Items);
        await using var otherDb = app.Database.CreateTenantContext(app.Stores[1].StoreId);
        Assert.Empty((await new InventoryLedgerIndexReadRepository(otherDb).QueryAsync(app.Stores[1].StoreId, new() { Keyword = "sua tuoi" }, null)).Items);
        Assert.Empty(db.ChangeTracker.Entries());
    }
}
