using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

internal static class ReceivingPackingBrowserFixture
{
    internal sealed record Seed(int ReceiptId, int PackLineId, int CartonLineId, int OtherLineId, int BaseUnitId, int ReceiveUnitId, string Before);
    internal static async Task<Seed> PrepareAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, int receiptId)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var original = await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == store.VariantId);
        var receipt = await db.StockDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == receiptId);
        var pack = receipt.Lines.Single(x => x.Factor == 4);
        var carton = receipt.Lines.Single(x => x.Factor == 24);
        var other = new ProductVariant { StoreId = store.StoreId, ProductId = original.ProductId, Sku = "UI05C-OTHER", ProductVariantName = "Sữa đóng lốc sáu hộp", IsActive = true };
        db.Add(other); await db.SaveChangesAsync();
        var conversion = new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = other.Id, UnitId = pack.UnitId!.Value, Factor = 6 };
        db.Add(conversion); await db.SaveChangesAsync();
        var line = new StockDocumentLine { StockDocumentId = receiptId, LineNo = 3, ProductVariantId = other.Id,
            ProductUnitConversionId = conversion.Id, UnitId = pack.UnitId, UnitNameSnapshot = "Lốc", ProductNameSnapshot = "Sữa đóng lốc sáu hộp kiểm tra phân biệt quy cách",
            Quantity = 3, BaseQuantity = 18, Factor = 6, UnitCost = 10, UnitPriceBeforeVat = 10, LineTotal = 30 };
        db.Add(line); await db.SaveChangesAsync();
        return new(receiptId, pack.Id, carton.Id, line.Id, original.Product.BaseUnitId, carton.UnitId!.Value, await ProtectedAsync(app, store));
    }
    private static async Task<string> ProtectedAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        return JsonSerializer.Serialize(new {
            balances = await db.InventoryBalances.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.OnHandQty, x.RowVersion }).ToListAsync(),
            postings = await db.InventoryTransactions.IgnoreQueryFilters().CountAsync(), payables = await db.PurchasePayables.IgnoreQueryFilters().CountAsync(),
            catalog = await db.ProductVariants.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.RowVersion }).ToListAsync(),
            conversions = await db.ProductUnitConversions.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Factor, x.RowVersion }).ToListAsync(),
            menus = await db.AdminMenuItems.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ParentId, x.Title, x.PermissionCode, x.Url, x.SortOrder, x.IsActive, x.RowVersion }).ToListAsync()
        });
    }
    internal static async Task<bool> VerifyAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, Seed seed)
    {
        Assert.Equal(seed.Before, await ProtectedAsync(app, store));
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var document = await db.StockDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == seed.ReceiptId);
        Assert.Equal(StockDocumentStatus.Draft, document.Status);
        var pack = Assert.Single(document.Lines, x => x.Id == seed.PackLineId); Assert.Equal(2.5m, pack.Quantity); Assert.Equal(10m, pack.BaseQuantity);
        Assert.Equal(2m, Assert.Single(document.Lines, x => x.Id == seed.CartonLineId).Quantity);
        Assert.Equal(3m, Assert.Single(document.Lines, x => x.Id == seed.OtherLineId).Quantity);
        var item = await db.StockDocumentProvisionalItems.SingleAsync(x => x.StockDocumentId == seed.ReceiptId);
        Assert.Equal(StockDocumentProvisionalItemStatus.Unresolved, item.Status); Assert.Equal(2.5m, item.Quantity); Assert.Equal(12m, item.ProposedFactor);
        Assert.NotNull(item.PackagingPhoto); Assert.True(item.PackagingPhoto.Length > 100); Assert.NotNull(item.ReviewDraftJson);
        using var draft = JsonDocument.Parse(item.ReviewDraftJson);
        var name = draft.RootElement.TryGetProperty("name", out var lower) ? lower : draft.RootElement.GetProperty("Name");
        Assert.Equal("Hàng mới đã kiểm tra UI05c", name.GetString());
        var output = Path.Combine(FullApplicationFixture.SourceRoot(), "TestResults", "mobile-ui-05c-20261025", "browser"); Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "sql-checks.json"), JsonSerializer.Serialize(new {
            passed = true, receiptId = seed.ReceiptId, status = document.Status.ToString(), knownLines = document.Lines.Count,
            packQuantity = pack.Quantity, packBaseQuantity = pack.BaseQuantity, provisionalQuantity = item.Quantity, provisionalFactor = item.ProposedFactor,
            photoBytes = item.PackagingPhoto.Length, draftName = name.GetString(), catalogInventoryMenusUnchanged = true
        }, new JsonSerializerOptions { WriteIndented = true }));
        return true;
    }
}
