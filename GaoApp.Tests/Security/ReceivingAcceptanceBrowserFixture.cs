using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

// Synthetic receipts and accounts in the existing disposable GUID database only.
internal static class ReceivingAcceptanceBrowserFixture
{
    internal sealed record Seed(int ReceiptId, int PackLineId, int OtherLineId, int ReadOnlyId, int ForeignId,
        FullApplicationFixture.Account Denied, string Before);

    internal static async Task<Seed> PrepareAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, int receiptId)
    {
        var denied = await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.View);
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var original = await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == store.VariantId);
        var receipt = await db.StockDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == receiptId);
        var pack = receipt.Lines.Single(x => x.Factor == 4);
        pack.Quantity = 1.125m; pack.BaseQuantity = 4.5m; pack.LineTotal = 11.25m;
        var other = new ProductVariant { StoreId = store.StoreId, ProductId = original.ProductId, Sku = "UI05E-PACK6",
            ProductVariantName = "Sữa đóng lốc sáu hộp kiểm tra cùng tên đơn vị", IsActive = true };
        db.Add(other); await db.SaveChangesAsync();
        var conversion = new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = other.Id, UnitId = pack.UnitId!.Value, Factor = 6 };
        db.Add(conversion); await db.SaveChangesAsync();
        db.Add(new ProductVariantUnitBarcode { StoreId = store.StoreId, ProductUnitConversionId = conversion.Id,
            Barcode = "UI05E-PACK6", BarcodeType = BarcodeType.Internal, IsPrimary = true, IsActive = true });
        var line = new StockDocumentLine { StockDocumentId = receiptId, LineNo = 3, ProductVariantId = other.Id,
            ProductUnitConversionId = conversion.Id, UnitId = pack.UnitId, UnitNameSnapshot = "Lốc", ProductNameSnapshot = other.ProductVariantName,
            Quantity = 1.125m, BaseQuantity = 6.75m, Factor = 6, UnitCost = 10, UnitPriceBeforeVat = 10, LineTotal = 11.25m };
        var readOnly = new StockDocument { StoreId = store.StoreId, WarehouseId = store.WarehouseId, DocumentNo = "UI05E-READONLY",
            DocumentTitle = "Phiếu chờ duyệt kiểm tra chỉ xem", Status = StockDocumentStatus.PendingApproval,
            Lines = [new StockDocumentLine { ProductVariantId = original.Id, ProductUnitConversionId = pack.ProductUnitConversionId,
                UnitId = pack.UnitId, UnitNameSnapshot = "Lốc", ProductNameSnapshot = "Sữa chỉ xem", Factor = 4,
                Quantity = 2, BaseQuantity = 8, UnitCost = 10, UnitPriceBeforeVat = 10, LineTotal = 20, LineNo = 1 }] };
        db.AddRange(line, readOnly); await db.SaveChangesAsync();
        var foreignStore = app.Stores[1];
        await using var foreignDb = app.Database.CreateTenantContext(foreignStore.StoreId);
        var foreign = new StockDocument { StoreId = foreignStore.StoreId, WarehouseId = foreignStore.WarehouseId, DocumentNo = "UI05E-FOREIGN" };
        foreignDb.Add(foreign); await foreignDb.SaveChangesAsync();
        return new(receiptId, pack.Id, line.Id, readOnly.Id, foreign.Id, denied, await ProtectedAsync(app, store, receiptId));
    }

    private static async Task<string> ProtectedAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, int receiptId)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        return JsonSerializer.Serialize(new {
            balances = await db.InventoryBalances.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.OnHandQty, x.RowVersion }).ToListAsync(),
            postings = await db.InventoryTransactions.IgnoreQueryFilters().CountAsync(), payables = await db.PurchasePayables.IgnoreQueryFilters().CountAsync(),
            catalog = await db.ProductVariants.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.RowVersion }).ToListAsync(),
            units = await db.Units.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Name, x.RowVersion }).ToListAsync(),
            barcodes = await db.ProductVariantUnitBarcodes.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Barcode, x.RowVersion }).ToListAsync(),
            conversions = await db.ProductUnitConversions.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Factor, x.RowVersion }).ToListAsync(),
            menus = await db.AdminMenuItems.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Title, x.Url, x.PermissionCode, x.SortOrder, x.IsActive, x.RowVersion }).ToListAsync(),
            otherDocuments = await db.StockDocuments.IgnoreQueryFilters().AsNoTracking().Where(x => x.Id != receiptId).OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.RowVersion }).ToListAsync(),
            otherLines = await db.StockDocumentLines.IgnoreQueryFilters().AsNoTracking().Where(x => x.StockDocumentId != receiptId).OrderBy(x => x.Id).Select(x => new { x.Id, x.Quantity, x.Factor, x.RowVersion }).ToListAsync()
        });
    }

    internal static async Task<bool> VerifyAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, Seed seed)
    {
        Assert.Equal(seed.Before, await ProtectedAsync(app, store, seed.ReceiptId));
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var receipt = await db.StockDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == seed.ReceiptId);
        Assert.Equal(StockDocumentStatus.Draft, receipt.Status); Assert.Equal(4, receipt.Lines.Count);
        var pack = Assert.Single(receipt.Lines, x => x.Id == seed.PackLineId);
        var other = Assert.Single(receipt.Lines, x => x.Id == seed.OtherLineId);
        Assert.Equal(.125m, pack.Quantity); Assert.Equal(.5m, pack.BaseQuantity);
        Assert.Equal(.125m, other.Quantity); Assert.Equal(.75m, other.BaseQuantity);
        Assert.Equal(pack.UnitId, other.UnitId); Assert.NotEqual(pack.ProductUnitConversionId, other.ProductUnitConversionId);
        Assert.Equal(2m, Assert.Single(receipt.Lines, x => x.Factor == 24).Quantity);
        var boxes = Assert.Single(receipt.Lines, x => x.Factor == 1);
        Assert.Equal(.251m, boxes.Quantity); Assert.Equal(.251m, boxes.BaseQuantity);
        var items = await db.StockDocumentProvisionalItems.Where(x => x.StockDocumentId == seed.ReceiptId).ToListAsync();
        var unknown = Assert.Single(items, x => x.RawBarcodeSnapshot == "UI05E-UNKNOWN");
        Assert.Equal(StockDocumentProvisionalItemStatus.Unresolved, unknown.Status); Assert.Equal(.125m, unknown.Quantity); Assert.Equal(12m, unknown.ProposedFactor);
        var known = items.Where(x => x.RawBarcodeSnapshot == "INTERNAL-BOX").OrderBy(x => x.Id).ToList();
        Assert.Equal(3, known.Count); Assert.Equal(new[] { .125m, .125m, .001m }, known.Select(x => x.Quantity).ToArray());
        var output = Path.Combine(FullApplicationFixture.SourceRoot(), "TestResults", "mobile-ui-05e-20261025", "browser"); Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "sql-checks.json"), JsonSerializer.Serialize(new {
            passed = true, receiptId = seed.ReceiptId, status = receipt.Status.ToString(), knownLines = receipt.Lines.Count,
            packQuantity = pack.Quantity, packBaseQuantity = pack.BaseQuantity, otherQuantity = other.Quantity, otherBaseQuantity = other.BaseQuantity,
            repeatedBarcodeCaptures = known.Count, boxesQuantity = boxes.Quantity, unknownQuantity = unknown.Quantity, unknownFactor = unknown.ProposedFactor,
            catalogInventoryMenusOtherReceiptsUnchanged = true
        }, new JsonSerializerOptions { WriteIndented = true }));
        return true;
    }
}
