using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.StoreMonitor;

/// <summary>Read-only work identity, explicitly constrained to the authenticated store.</summary>
public static class StoreActivityWorkContext
{
    public sealed record Work(string Key, string Document);
    public static async Task<Work?> Page(AppDbContext db, int storeId, string controller, int? id, CancellationToken ct)
    {
        if (id is not > 0) return null;
        var found = id.Value;
        var (kind, caption, exists) = controller switch
        {
            "WarehouseReceiving" or "PurchaseReceiving" or "StockDocumentManagement" =>
                ("receipt", "Phiếu nhập", await db.StockDocuments.AsNoTracking().AnyAsync(x => x.StoreId == storeId && x.Id == found, ct)),
            "StockCountPages" =>
                ("count", "Kiểm kê", await db.StockCountDocuments.AsNoTracking().AnyAsync(x => x.StoreId == storeId && x.Id == found, ct)),
            "StockTransfer" =>
                ("transfer", "Chuyển kho", await db.StockTransferDocuments.AsNoTracking().AnyAsync(x => x.StoreId == storeId && x.Id == found, ct)),
            "InventoryAdjustmentDocuments" =>
                ("adjustment", "Điều chỉnh", await db.InventoryAdjustmentDocuments.AsNoTracking().AnyAsync(x => x.StoreId == storeId && x.Id == found, ct)),
            "LabelPrinting" =>
                ("label", "Phiếu tem", await db.Set<ProductLabelTask>().AsNoTracking().AnyAsync(x => x.StoreId == storeId && x.Id == found, ct)),
            _ => ("", "", false)
        };
        return exists ? new($"{kind}:{found}", $"{caption} #{found}") : null;
    }
    // Line deletion cannot be resolved after the action, so read the parent before a known line write.
    public static async Task<Work?> Line(AppDbContext db, int storeId, string controller, int lineId, CancellationToken ct)
    {
        if (controller == "StockTransfers")
        {
            var transfer = await db.StockTransferLines.AsNoTracking().Where(x => x.StoreId == storeId && x.Id == lineId)
                .Select(x => (int?)x.StockTransferDocumentId).SingleOrDefaultAsync(ct);
            return transfer is { } id ? new($"transfer:{id}", $"Chuyển kho #{id}") : null;
        }
        var count = await db.StockCountLines.AsNoTracking().Where(x => x.StoreId == storeId && x.Id == lineId)
            .Select(x => (int?)x.StockCountDocumentId).SingleOrDefaultAsync(ct);
        return count is { } countId ? new($"count:{countId}", $"Kiểm kê #{countId}") : null;
    }
    public static async Task<Work?> LabelJob(AppDbContext db, int storeId, int jobId, CancellationToken ct)
    {
        var taskId = await db.Set<ProductLabelJob>().AsNoTracking().Where(x => x.StoreId == storeId && x.Id == jobId)
            .Select(x => x.TaskId).SingleOrDefaultAsync(ct);
        return taskId is { } id ? new($"label:{id}", $"Phiếu tem #{id}") : null;
    }
}
