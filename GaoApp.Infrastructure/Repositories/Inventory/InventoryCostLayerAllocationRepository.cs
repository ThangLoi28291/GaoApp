using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Inventory;

/// <summary>
/// Repository thao tác với allocation giữa valuation entry và FIFO cost layer.
/// </summary>
public class InventoryCostLayerAllocationRepository : IInventoryCostLayerAllocationRepository
{
    private readonly AppDbContext _db;

    public InventoryCostLayerAllocationRepository(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Thêm 1 allocation.
    /// </summary>
    public Task AddAsync(InventoryCostLayerAllocation entity, CancellationToken ct = default)
        => _db.InventoryCostLayerAllocations.AddAsync(entity, ct).AsTask();

    /// <summary>
    /// Thêm nhiều allocation.
    /// </summary>
    public Task AddRangeAsync(IEnumerable<InventoryCostLayerAllocation> entities, CancellationToken ct = default)
        => _db.InventoryCostLayerAllocations.AddRangeAsync(entities, ct);

    /// <summary>
    /// Lấy allocation theo id.
    /// </summary>
    public Task<InventoryCostLayerAllocation?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return _db.InventoryCostLayerAllocations
            .Include(x => x.InventoryValuationEntry)
            .Include(x => x.InventoryCostLayer)
            .Include(x => x.ResolvedByInventoryCostLayer)
            .Include(x => x.ReverseOfAllocation)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    /// <summary>
    /// Lấy toàn bộ allocation của 1 valuation entry.
    /// </summary>
    public Task<List<InventoryCostLayerAllocation>> GetByValuationEntryIdAsync(
        int valuationEntryId,
        CancellationToken ct = default)
    {
        return _db.InventoryCostLayerAllocations
            .Include(x => x.InventoryCostLayer)
            .Include(x => x.ResolvedByInventoryCostLayer)
            .Where(x => x.InventoryValuationEntryId == valuationEntryId)
            .OrderBy(x => x.Id)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Lấy các provisional allocations còn mở để inbound layer resolve dần theo FIFO thời gian outbound.
    /// </summary>
    public Task<List<InventoryCostLayerAllocation>> GetOpenProvisionalAllocationsAsync(
        int warehouseId,
        int productVariantId,
        CancellationToken ct = default)
    {
        return _db.InventoryCostLayerAllocations
            .Include(x => x.InventoryValuationEntry)
            .Where(x =>
                x.IsProvisional &&
                !x.IsResolved &&
                x.InventoryValuationEntry.WarehouseId == warehouseId &&
                x.InventoryValuationEntry.ProductVariantId == productVariantId &&
                x.InventoryValuationEntry.EntryType == InventoryValuationEntryType.Outbound)
            .OrderBy(x => x.InventoryValuationEntry.OccurredAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Bản tracked để update/resolve trong transaction.
    /// Hiện tại cùng logic với bản thường, nhưng tách riêng interface để sau này dễ nâng cấp locking.
    /// </summary>
    public Task<List<InventoryCostLayerAllocation>> GetOpenProvisionalAllocationsForUpdateAsync(
        int warehouseId,
        int productVariantId,
        CancellationToken ct = default)
    {
        return _db.InventoryCostLayerAllocations
            .Include(x => x.InventoryValuationEntry)
            .Where(x =>
                x.IsProvisional &&
                !x.IsResolved &&
                x.InventoryValuationEntry.WarehouseId == warehouseId &&
                x.InventoryValuationEntry.ProductVariantId == productVariantId &&
                x.InventoryValuationEntry.EntryType == InventoryValuationEntryType.Outbound)
            .OrderBy(x => x.InventoryValuationEntry.OccurredAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Lấy các allocation nguồn của order line vẫn còn outstanding
    /// sau khi trừ các reverse allocations (void/return).
    /// </summary>
    public async Task<List<InventoryCostLayerAllocation>> GetOutstandingSourceAllocationsForOrderLineAsync(
        int orderId,
        int orderLineId,
        CancellationToken ct = default)
    {
        var sourceAllocations = await _db.InventoryCostLayerAllocations
            .Include(x => x.InventoryValuationEntry)
            .Include(x => x.InventoryCostLayer)
            .Where(x =>
                x.InventoryValuationEntry.ReferenceType == InventoryReferenceType.Order &&
                x.InventoryValuationEntry.ReferenceId == orderId.ToString() &&
                x.InventoryValuationEntry.ReferenceLineId == orderLineId &&
                x.InventoryValuationEntry.EntryType == InventoryValuationEntryType.Outbound)
            .OrderBy(x => x.InventoryValuationEntry.OccurredAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);

        if (sourceAllocations.Count == 0)
            return sourceAllocations;

        var sourceAllocationIds = sourceAllocations.Select(x => x.Id).ToList();

        var reversedQtyMap = await _db.InventoryCostLayerAllocations
            .Where(x =>
                x.ReverseOfAllocationId.HasValue &&
                sourceAllocationIds.Contains(x.ReverseOfAllocationId.Value))
            .GroupBy(x => x.ReverseOfAllocationId!.Value)
            .Select(g => new
            {
                SourceAllocationId = g.Key,
                ReversedQuantity = g.Sum(x => x.Quantity)
            })
            .ToDictionaryAsync(x => x.SourceAllocationId, x => x.ReversedQuantity, ct);

        var result = new List<InventoryCostLayerAllocation>();

        foreach (var item in sourceAllocations)
        {
            reversedQtyMap.TryGetValue(item.Id, out var reversedQty);
            var remainingQty = item.Quantity - reversedQty;

            if (remainingQty <= 0)
                continue;

            // Clone nhẹ object theo chính entity hiện tại để service đọc thuận tiện.
            // Không tạo entity mới vào DbContext, chỉ trả object in-memory.
            result.Add(new InventoryCostLayerAllocation
            {
                Id = item.Id,
                StoreId = item.StoreId,
                InventoryValuationEntryId = item.InventoryValuationEntryId,
                InventoryValuationEntry = item.InventoryValuationEntry,
                InventoryCostLayerId = item.InventoryCostLayerId,
                InventoryCostLayer = item.InventoryCostLayer,
                ReverseOfAllocationId = item.ReverseOfAllocationId,
                Quantity = remainingQty,
                UnitCost = item.UnitCost,
                Amount = Math.Round(remainingQty * item.UnitCost, 4, MidpointRounding.AwayFromZero),
                IsProvisional = item.IsProvisional,
                IsResolved = item.IsResolved,
                ResolvedAtUtc = item.ResolvedAtUtc,
                ResolvedByInventoryCostLayerId = item.ResolvedByInventoryCostLayerId,
                Note = item.Note
            });
        }

        return result
            .OrderBy(x => x.InventoryValuationEntry.OccurredAtUtc)
            .ThenBy(x => x.Id)
            .ToList();
    }

    /// <summary>
    /// Lưu thay đổi.
    /// </summary>
    public Task SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}