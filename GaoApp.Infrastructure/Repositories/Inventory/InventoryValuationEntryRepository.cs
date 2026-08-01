using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Inventory;

/// <summary>
/// Repository đọc/ghi valuation entries phục vụ:
/// - costing layer-based
/// - provisional / revaluation
/// - sales return / sale void mức 2 theo source fragment
///
/// Lưu ý:
/// - Repository này chỉ query dữ liệu
/// - Không tự tính lại cost
/// - Không dùng average cost hiện tại
/// - SalesReturnService sẽ dùng các query ở đây để mirror từ valuation gốc
/// </summary>
public class InventoryValuationEntryRepository : IInventoryValuationEntryRepository
{
    private readonly AppDbContext _db;

    public InventoryValuationEntryRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task AddRangeAsync(IEnumerable<InventoryValuationEntry> entries, CancellationToken ct = default)
    {
        await _db.InventoryValuationEntries.AddRangeAsync(entries, ct);
    }

    public Task<List<InventoryValuationEntry>> GetByReferenceAsync(
        InventoryReferenceType referenceType,
        string referenceId,
        int? referenceLineId,
        CancellationToken ct = default)
    {
        return _db.InventoryValuationEntries
            .Where(x => !x.IsDeleted
                        && x.ReferenceType == referenceType
                        && x.ReferenceId == referenceId
                        && x.ReferenceLineId == referenceLineId)
            .OrderBy(x => x.OccurredAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
    }

    public Task<List<InventoryValuationEntry>> GetByInventoryTransactionIdAsync(
        int inventoryTransactionId,
        CancellationToken ct = default)
    {
        return _db.InventoryValuationEntries
            .Where(x => !x.IsDeleted
                        && x.InventoryTransactionId == inventoryTransactionId)
            .OrderBy(x => x.OccurredAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
    }

    public Task<List<InventoryValuationEntry>> GetByInventoryTransactionIdAsync(
        int storeId,
        int inventoryTransactionId,
        CancellationToken ct = default)
    {
        return _db.InventoryValuationEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.StoreId == storeId
                        && !x.IsDeleted
                        && x.InventoryTransactionId == inventoryTransactionId)
            .OrderBy(x => x.OccurredAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
    }

    public Task<List<InventoryValuationEntry>> GetSaleIssueEntriesByOrderLineAsync(
        int orderId,
        int orderLineId,
        CancellationToken ct = default)
    {
        return _db.InventoryValuationEntries
            .Where(x => !x.IsDeleted
                        && x.ReferenceType == InventoryReferenceType.Order
                        && x.ReferenceId == orderId.ToString()
                        && x.ReferenceLineId == orderLineId
                        && x.EntryType == InventoryValuationEntryType.Outbound
                        && x.Quantity < 0)
            .OrderBy(x => x.OccurredAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
    }

    public Task<List<InventoryValuationEntry>> GetReverseEntriesBySourceEntryIdAsync(
        int sourceValuationEntryId,
        CancellationToken ct = default)
    {
        return _db.InventoryValuationEntries
            .Where(x => !x.IsDeleted
                        && x.SourceValuationEntryId == sourceValuationEntryId)
            .OrderBy(x => x.OccurredAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
    }

    public Task<InventoryValuationEntry?> GetByIdAsync(
        int id,
        CancellationToken ct = default)
    {
        return _db.InventoryValuationEntries
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, ct);
    }

    public Task<bool> ExistsByReferenceSubKeyAsync(
        InventoryReferenceType referenceType,
        string referenceId,
        int? referenceLineId,
        string? referenceSubKey,
        InventoryValuationEntryType entryType,
        CancellationToken ct = default)
    {
        return _db.InventoryValuationEntries.AnyAsync(x =>
            !x.IsDeleted
            && x.ReferenceType == referenceType
            && x.ReferenceId == referenceId
            && x.ReferenceLineId == referenceLineId
            && x.ReferenceSubKey == referenceSubKey
            && x.EntryType == entryType, ct);
    }

    public Task<List<InventoryValuationEntry>> GetOpenProvisionalOutboundEntriesAsync(
        int warehouseId,
        int productVariantId,
        CancellationToken ct = default)
    {
        return _db.InventoryValuationEntries
            .Where(x => !x.IsDeleted
                        && x.WarehouseId == warehouseId
                        && x.ProductVariantId == productVariantId
                        && x.EntryType == InventoryValuationEntryType.Outbound
                        && x.IsProvisional
                        && x.CostFinalizedAtUtc == null)
            .OrderBy(x => x.OccurredAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Lấy toàn bộ valuation của dòng bán gốc:
    /// - outbound sale issue fragments
    /// - các revaluation đi theo chính dòng đó
    ///
    /// Dùng cho SalesReturn mức 2 để mirror:
    /// - actual outbound gốc
    /// - provisional/revaluation trace nếu có
    ///
    /// Không dùng average cost hiện tại.
    /// </summary>
    public Task<List<InventoryValuationEntry>> GetSaleIssueAndRevaluationEntriesByOrderLineAsync(
        int orderId,
        int orderLineId,
        CancellationToken ct = default)
    {
        return _db.InventoryValuationEntries
            .Where(x => !x.IsDeleted
                        && x.ReferenceType == InventoryReferenceType.Order
                        && x.ReferenceId == orderId.ToString()
                        && x.ReferenceLineId == orderLineId
                        && (
                            (x.EntryType == InventoryValuationEntryType.Outbound && x.Quantity < 0)
                            || x.EntryType == InventoryValuationEntryType.Revaluation
                        ))
            .OrderBy(x => x.OccurredAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Lấy riêng revaluation entries của dòng bán gốc.
    /// 
    /// Mục tiêu:
    /// - biết dòng bán có bị provisional rồi revalue hay không
    /// - sau này SalesReturnService mirror trace đúng
    /// </summary>
    public Task<List<InventoryValuationEntry>> GetSaleIssueRevaluationEntriesByOrderLineAsync(
        int orderId,
        int orderLineId,
        CancellationToken ct = default)
    {
        return _db.InventoryValuationEntries
            .Where(x => !x.IsDeleted
                        && x.ReferenceType == InventoryReferenceType.Order
                        && x.ReferenceId == orderId.ToString()
                        && x.ReferenceLineId == orderLineId
                        && x.EntryType == InventoryValuationEntryType.Revaluation)
            .OrderBy(x => x.OccurredAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Kiểm tra dòng bán gốc có từng phát sinh provisional valuation hay không.
    ///
    /// Rule ở đây:
    /// - chỉ cần có 1 outbound hoặc revaluation thuộc order line được đánh dấu provisional
    ///   thì coi như dòng bán này có trace provisional
    /// </summary>
    public Task<bool> HasProvisionalValuationByOrderLineAsync(
        int orderId,
        int orderLineId,
        CancellationToken ct = default)
    {
        return _db.InventoryValuationEntries.AnyAsync(x =>
            !x.IsDeleted
            && x.ReferenceType == InventoryReferenceType.Order
            && x.ReferenceId == orderId.ToString()
            && x.ReferenceLineId == orderLineId
            && x.IsProvisional, ct);
    }

    /// <summary>
    /// Tính tổng quantity đã reverse từ 1 source entry.
    ///
    /// Ý nghĩa:
    /// - source outbound fragment đã bị return/void/reverse bao nhiêu rồi
    /// - dùng để biết còn lại bao nhiêu mà mirror tiếp
    ///
    /// Quy ước:
    /// - reverse của outbound âm thường là inbound dương
    /// - nhưng để an toàn audit, ở repository chỉ cộng toàn bộ quantity của các entry reverse
    /// - service layer sẽ dùng magnitude phù hợp theo business rule
    /// </summary>
    public async Task<decimal> GetTotalReversedQuantityBySourceEntryIdAsync(
        int sourceValuationEntryId,
        CancellationToken ct = default)
    {
        var total = await _db.InventoryValuationEntries
            .Where(x => !x.IsDeleted
                        && x.SourceValuationEntryId == sourceValuationEntryId)
            .SumAsync(x => (decimal?)x.Quantity, ct);

        return total ?? 0m;
    }

    /// <summary>
    /// Tính tổng amount đã reverse từ 1 source entry.
    ///
    /// Dùng cho audit/đối soát mirror valuation theo fragment.
    /// Không tự tính lại amount theo cost hiện tại.
    /// </summary>
    public async Task<decimal> GetTotalReversedAmountBySourceEntryIdAsync(
        int sourceValuationEntryId,
        CancellationToken ct = default)
    {
        var total = await _db.InventoryValuationEntries
            .Where(x => !x.IsDeleted
                        && x.SourceValuationEntryId == sourceValuationEntryId)
            .SumAsync(x => (decimal?)x.Amount, ct);

        return total ?? 0m;
    }
    public Task<List<InventoryValuationEntry>> GetRevaluationEntriesBySourceIdAsync(
    int sourceValuationEntryId,
    CancellationToken ct = default)
    {
        return _db.InventoryValuationEntries
            .Where(x => !x.IsDeleted
                        && x.EntryType == InventoryValuationEntryType.Revaluation
                        && x.RevaluationOfEntryId == sourceValuationEntryId)
            .ToListAsync(ct);
    }
}
