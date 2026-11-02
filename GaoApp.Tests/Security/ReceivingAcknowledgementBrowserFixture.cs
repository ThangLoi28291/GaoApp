using System.Text.Json;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

internal static class ReceivingAcknowledgementBrowserFixture
{
    internal sealed record Seed(int ReceiptId, string Before);
    internal static async Task<Seed> PrepareAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, int receiptId)
        => new(receiptId, await ProtectedAsync(app, store, receiptId));

    private static async Task<string> ProtectedAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, int receiptId)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        return JsonSerializer.Serialize(new {
            balances = await db.InventoryBalances.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.OnHandQty, x.RowVersion }).ToListAsync(),
            postings = await db.InventoryTransactions.IgnoreQueryFilters().CountAsync(), payables = await db.PurchasePayables.IgnoreQueryFilters().CountAsync(),
            catalog = await db.ProductVariants.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.RowVersion }).ToListAsync(),
            conversions = await db.ProductUnitConversions.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Factor, x.RowVersion }).ToListAsync(),
            menus = await db.AdminMenuItems.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Title, x.Url, x.PermissionCode, x.SortOrder, x.IsActive, x.RowVersion }).ToListAsync(),
            otherDocuments = await db.StockDocuments.IgnoreQueryFilters().AsNoTracking().Where(x => x.Id != receiptId).OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.RowVersion }).ToListAsync()
        });
    }

    internal static async Task<bool> VerifyAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, Seed seed)
    {
        Assert.Equal(seed.Before, await ProtectedAsync(app, store, seed.ReceiptId));
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var receipt = await db.StockDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == seed.ReceiptId);
        Assert.Equal(StockDocumentStatus.Draft, receipt.Status);
        Assert.Equal(3, receipt.Lines.Count);
        Assert.All(receipt.Lines.Where(x => x.Factor != 1), x => Assert.Equal(2m, x.Quantity));
        var added = Assert.Single(receipt.Lines, x => x.Factor == 1);
        Assert.Equal(2.75m, added.Quantity); Assert.Equal(2.75m, added.BaseQuantity);
        var provisional = await db.StockDocumentProvisionalItems.Where(x => x.StockDocumentId == seed.ReceiptId).ToListAsync();
        var unknown = Assert.Single(provisional, x => x.RawBarcodeSnapshot == "UI05D-UNKNOWN");
        Assert.Equal(2.5m, unknown.Quantity); Assert.Equal(12m, unknown.ProposedFactor);
        Assert.Equal("Hàng kiểm tra xác nhận UI05d", unknown.NameSnapshot);
        Assert.Equal("Ghi chú giữ nguyên khi thử lại", unknown.Note);
        Assert.NotNull(unknown.PackagingPhoto); Assert.True(unknown.PackagingPhoto.Length > 100);
        Assert.Equal(StockDocumentProvisionalItemStatus.Unresolved, unknown.Status);
        var output = Path.Combine(FullApplicationFixture.SourceRoot(), "TestResults", "mobile-ui-05d-20261025", "browser");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "sql-checks.json"), JsonSerializer.Serialize(new {
            passed = true, receiptId = seed.ReceiptId, status = receipt.Status.ToString(), knownLines = receipt.Lines.Count,
            baseQuantity = added.Quantity, unknownQuantity = unknown.Quantity, unknownFactor = unknown.ProposedFactor,
            photoBytes = unknown.PackagingPhoto.Length, catalogInventoryMenusOtherDocumentsUnchanged = true
        }, new JsonSerializerOptions { WriteIndented = true }));
        return true;
    }
}
