using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

// Extends only the existing GUID browser fixture; never reads a configured store database.
internal static class ReceivingDetailV2BrowserFixture
{
    internal sealed record Seed(int ReceiptId, int EmptyId, int RejectedId, FullApplicationFixture.Account Denied, string Before);
    internal static async Task<Seed> PrepareAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, int receiptId)
    {
        var denied = await app.AddAccountAsync(store, PermissionCodes.Inventory.StockDocument.View);
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var receipt = await db.StockDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == receiptId);
        receipt.DocumentTitle = "Nhập sữa và hàng tiêu dùng cho chương trình cuối tháng <kiểm tra> & đối chiếu tại cửa hàng";
        receipt.Lines.Last().ProductNameSnapshot = "Sữa dinh dưỡng dành cho gia đình quy cách đóng gói kiểm tra tên sản phẩm dài trên điện thoại";
        var empty = new StockDocument { StoreId = store.StoreId, WarehouseId = store.WarehouseId, DocumentNo = "UI05B-EMPTY", DocumentTitle = "Phiếu chưa có hàng" };
        var rejected = new StockDocument { StoreId = store.StoreId, WarehouseId = store.WarehouseId, DocumentNo = "UI05B-REJECTED", DocumentTitle = "Phiếu cần kiểm đếm lại",
            Status = StockDocumentStatus.Rejected, ApprovalNote = "Kiểm tra lại quy cách và số lượng thực nhận." };
        db.AddRange(empty, rejected); await db.SaveChangesAsync();
        return new(receiptId, empty.Id, rejected.Id, denied, await ProtectedStateAsync(app, store, receiptId));
    }
    private static async Task<string> ProtectedStateAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, int receiptId)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        return JsonSerializer.Serialize(new {
            documents = await db.StockDocuments.IgnoreQueryFilters().AsNoTracking().Where(x => x.Id != receiptId).OrderBy(x => x.Id).Select(x => new { x.Id, x.RowVersion }).ToListAsync(),
            balances = await db.InventoryBalances.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.OnHandQty, x.RowVersion }).ToListAsync(),
            postings = await db.InventoryTransactions.IgnoreQueryFilters().CountAsync(), payables = await db.PurchasePayables.IgnoreQueryFilters().CountAsync(),
            menus = await db.AdminMenuItems.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ParentId, x.Title, x.Url, x.Area, x.Controller, x.Action, x.Icon, x.PermissionCode, x.SortOrder, x.IsActive, x.IsDeleted, x.RowVersion }).ToListAsync()
        });
    }
    internal static async Task<bool> VerifyAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, Seed seed)
    {
        Assert.Equal(seed.Before, await ProtectedStateAsync(app, store, seed.ReceiptId));
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var document = await db.StockDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == seed.ReceiptId);
        Assert.Equal(StockDocumentStatus.PendingApproval, document.Status);
        var added = Assert.Single(document.Lines, x => !x.IsDeleted && x.Factor == 1);
        Assert.Equal(3m, added.Quantity); Assert.Equal(3m, added.BaseQuantity);
        Assert.Equal(23, document.Lines.Count(x => !x.IsDeleted));
        var output = Path.Combine(FullApplicationFixture.SourceRoot(), "TestResults", "mobile-ui-05b-20261025", "browser");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "sql-checks.json"), JsonSerializer.Serialize(new {
            passed = true, receiptId = seed.ReceiptId, status = document.Status.ToString(), lineCount = 23,
            addedLineId = added.Id, added.Quantity, added.BaseQuantity, inventoryMenusOtherDocumentsUnchanged = true
        }, new JsonSerializerOptions { WriteIndented = true }));
        return true;
    }
}
