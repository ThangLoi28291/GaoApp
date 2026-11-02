using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Storefront;

internal static class StorefrontValuationEvidence
{
    internal sealed record Graph(IReadOnlyList<InventoryValuationEntry> Roots, IReadOnlyList<InventoryValuationEntry> Events)
    {
        internal SaleValuationCostPolicy.Result Evaluate(InventoryValuationEntry root)
        {
            var events = Events.Where(x => x.RevaluationOfEntryId == root.Id || x.SourceValuationEntryId == root.Id).ToList();
            return SaleValuationCostPolicy.EvaluateDeliveryBudget(root, events.Where(x => x.EntryType == InventoryValuationEntryType.Revaluation).ToList(), events);
        }
    }
    internal static async Task<Graph> ReadAsync(AppDbContext db, IEnumerable<int> transactionIds, CancellationToken ct)
    {
        var store = db.CurrentStoreId ?? throw new ForbiddenAppException("Thiếu phạm vi nguồn giá vốn.");
        var ids = transactionIds.Distinct().ToList(); if (ids.Count > 20000) throw new ConflictAppException("Quá nhiều nguồn giá vốn trong một lệnh.");
        var roots = new List<InventoryValuationEntry>();
        foreach (var batch in ids.Chunk(1000))
            roots.AddRange(await db.InventoryValuationEntries.AsNoTracking().AsSplitQuery().Include(x => x.InventoryTransaction).Include(x => x.Warehouse).Include(x => x.ProductVariant)
                .Include(x => x.CostLayerAllocations).Include(x => x.CostBudgetDebits).ThenInclude(x => x.Layer)
                .Where(x => x.StoreId == store && batch.Contains(x.InventoryTransactionId) && x.EntryType == InventoryValuationEntryType.Outbound).ToListAsync(ct));
        if (roots.Count > 100000) throw new ConflictAppException("Nguồn giá vốn quá phân mảnh; cần cửa hàng đối soát trước.");
        var events = new List<InventoryValuationEntry>();
        foreach (var batch in roots.Select(x => x.Id).Chunk(1000))
            events.AddRange(await db.InventoryValuationEntries.AsNoTracking().Include(x => x.InventoryTransaction).Include(x => x.InventoryCostLayer).Include(x => x.Warehouse).Include(x => x.ProductVariant)
                .Where(x => x.StoreId == store && (x.RevaluationOfEntryId.HasValue && batch.Contains(x.RevaluationOfEntryId.Value) || x.SourceValuationEntryId.HasValue && batch.Contains(x.SourceValuationEntryId.Value))).ToListAsync(ct));
        if (events.Count > 100000) throw new ConflictAppException("Quá nhiều điều chỉnh nguồn; cần cửa hàng đối soát trước.");
        return new(roots, events.DistinctBy(x => x.Id).ToList());
    }
}
