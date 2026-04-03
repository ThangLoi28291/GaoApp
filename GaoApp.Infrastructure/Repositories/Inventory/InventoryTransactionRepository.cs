using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Inventory;

public sealed class InventoryTransactionRepository : IInventoryTransactionRepository
{
    private readonly AppDbContext _db;

    public InventoryTransactionRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task AddAsync(InventoryTransaction transaction, CancellationToken ct = default)
        => _db.InventoryTransactions.AddAsync(transaction, ct).AsTask();

    public Task AddRangeAsync(IEnumerable<InventoryTransaction> transactions, CancellationToken ct = default)
        => _db.InventoryTransactions.AddRangeAsync(transactions, ct);

    public async Task<List<InventoryTransaction>> GetByVariantAsync(int productVariantId, CancellationToken ct = default)
    {
        return await _db.InventoryTransactions
            .Where(x => x.ProductVariantId == productVariantId)
            .OrderByDescending(x => x.OccurredAtUtc)
            .ToListAsync(ct);
    }

    public Task<bool> ExistsAsync(
      int warehouseId,
      int productVariantId,
      InventoryTransactionType transactionType,
      InventoryReferenceType referenceType,
      string? referenceId,
      int? referenceLineId,
      string? referenceSubKey,
      CancellationToken ct = default)
    {
        return _db.InventoryTransactions.AnyAsync(x =>
            !x.IsDeleted &&
            x.WarehouseId == warehouseId &&
            x.ProductVariantId == productVariantId &&
            x.TransactionType == transactionType &&
            x.ReferenceType == referenceType &&
            x.ReferenceId == referenceId &&
            x.ReferenceLineId == referenceLineId &&
            x.ReferenceSubKey == referenceSubKey,
            ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);

    public async Task<List<InventoryTransaction>> GetAdjustmentTransactionsAsync(CancellationToken ct = default)
    {
        return await _db.InventoryTransactions
            .Include(x => x.Warehouse)
            .Include(x => x.ProductVariant)
                .ThenInclude(x => x.Product)
            .Where(x =>
                x.TransactionType == InventoryTransactionType.AdjustmentIncrease ||
                x.TransactionType == InventoryTransactionType.AdjustmentDecrease)
            .OrderByDescending(x => x.OccurredAtUtc)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Query thẻ kho / ledger kho.
    /// Hỗ trợ:
    /// - lọc theo kho
    /// - lọc theo variant
    /// - lọc theo keyword
    /// - lọc theo loại giao dịch
    /// - lọc theo loại chứng từ tham chiếu
    /// - lọc theo mã chứng từ tham chiếu
    /// - lọc theo khoảng ngày
    /// - lọc các giao dịch có tồn âm sau giao dịch
    /// - sort
    /// - phân trang
    ///
    /// Ghi chú barcode:
    /// - Không còn ProductVariant.Barcode
    /// - Filter barcode đi qua UnitConversions -> Barcodes
    /// - Include đủ navigation để service có thể resolve barcode đại diện
    /// </summary>
    public async Task<(List<InventoryTransaction> Items, int TotalItems)> QueryLedgerAsync(
        InventoryLedgerQueryRequest request,
        CancellationToken ct = default)
    {
        // Chuẩn hóa page/pageSize để tránh lỗi chia trang.
        var page = request.Page <= 0 ? 1 : request.Page;
        var pageSize = request.PageSize <= 0 ? 20 : request.PageSize;

        // Chuẩn hóa keyword/referenceId để tránh khoảng trắng dư.
        var keyword = string.IsNullOrWhiteSpace(request.Keyword)
            ? null
            : request.Keyword.Trim();

        var referenceId = string.IsNullOrWhiteSpace(request.ReferenceId)
            ? null
            : request.ReferenceId.Trim();

        // Quy ước date filter:
        // - FromDate: lấy từ đầu ngày
        // - ToDate: lấy đến trước ngày kế tiếp
        DateTime? fromUtc = request.FromDate?.Date;
        DateTime? toUtcExclusive = request.ToDate?.Date.AddDays(1);

        var query = _db.InventoryTransactions
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.ProductVariant)
                .ThenInclude(x => x.Product)
            // Include đủ để service resolve barcode đại diện
            .Include(x => x.ProductVariant)
                .ThenInclude(x => x.UnitConversions.Where(c => !c.IsDeleted && c.IsActive))
                    .ThenInclude(c => c.Barcodes.Where(b => !b.IsDeleted && b.IsActive))
            .AsQueryable();

        // 1. Filter theo kho
        if (request.WarehouseId.HasValue && request.WarehouseId.Value > 0)
        {
            query = query.Where(x => x.WarehouseId == request.WarehouseId.Value);
        }

        // 2. Filter theo variant
        if (request.ProductVariantId.HasValue && request.ProductVariantId.Value > 0)
        {
            query = query.Where(x => x.ProductVariantId == request.ProductVariantId.Value);
        }

        // 3. Filter theo loại giao dịch
        if (request.TransactionType.HasValue)
        {
            query = query.Where(x => x.TransactionType == request.TransactionType.Value);
        }

        // 4. Filter theo loại chứng từ tham chiếu
        if (request.ReferenceType.HasValue)
        {
            query = query.Where(x => x.ReferenceType == request.ReferenceType.Value);
        }

        // 5. Filter theo mã / id chứng từ tham chiếu
        if (!string.IsNullOrWhiteSpace(referenceId))
        {
            query = query.Where(x => x.ReferenceId != null && x.ReferenceId.Contains(referenceId));
        }

        // 6. Filter theo ngày
        if (fromUtc.HasValue)
        {
            query = query.Where(x => x.OccurredAtUtc >= fromUtc.Value);
        }

        if (toUtcExclusive.HasValue)
        {
            query = query.Where(x => x.OccurredAtUtc < toUtcExclusive.Value);
        }

        // 7. Chỉ lấy các giao dịch mà sau giao dịch tồn bị âm
        if (request.OnlyNegativeAfterTransaction)
        {
            query = query.Where(x => x.AfterQty < 0);
        }

        // 8. Filter theo keyword
        // Keyword hiện hỗ trợ:
        // - tên sản phẩm
        // - SKU
        // - barcode đơn vị
        // - ReferenceId
        // - Note
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(x =>
                (x.ProductVariant.Product != null && x.ProductVariant.Product.Name.Contains(keyword)) ||
                (x.ProductVariant.Sku != null && x.ProductVariant.Sku.Contains(keyword)) ||
                x.ProductVariant.UnitConversions.Any(c =>
                    !c.IsDeleted &&
                    c.IsActive &&
                    c.Barcodes.Any(b =>
                        !b.IsDeleted &&
                        b.IsActive &&
                        b.Barcode.Contains(keyword))) ||
                (x.ReferenceId != null && x.ReferenceId.Contains(keyword)) ||
                (x.Note != null && x.Note.Contains(keyword)));
        }

        // 9. Sort
        query = ApplyLedgerSort(query, request.SortBy, request.SortDirection);

        var totalItems = await query.CountAsync(ct);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalItems);
    }

    /// <summary>
    /// Áp dụng sort cho query ledger.
    /// Hỗ trợ các cột sort phổ biến để phục vụ UI/admin report.
    /// </summary>
    private static IQueryable<InventoryTransaction> ApplyLedgerSort(
        IQueryable<InventoryTransaction> query,
        string? sortBy,
        string? sortDirection)
    {
        var normalizedSortBy = string.IsNullOrWhiteSpace(sortBy)
            ? "OccurredAtUtc"
            : sortBy.Trim();

        var normalizedSortDirection = string.IsNullOrWhiteSpace(sortDirection)
            ? "desc"
            : sortDirection.Trim().ToLowerInvariant();

        var isAsc = normalizedSortDirection == "asc";

        switch (normalizedSortBy.Trim().ToLowerInvariant())
        {
            case "productname":
                return isAsc
                    ? query.OrderBy(x => x.ProductVariant.Product.Name).ThenBy(x => x.Id)
                    : query.OrderByDescending(x => x.ProductVariant.Product.Name).ThenByDescending(x => x.Id);

            case "sku":
                return isAsc
                    ? query.OrderBy(x => x.ProductVariant.Sku).ThenBy(x => x.Id)
                    : query.OrderByDescending(x => x.ProductVariant.Sku).ThenByDescending(x => x.Id);

            case "warehouse":
                return isAsc
                    ? query.OrderBy(x => x.Warehouse.Name).ThenBy(x => x.Id)
                    : query.OrderByDescending(x => x.Warehouse.Name).ThenByDescending(x => x.Id);

            case "transactiontype":
                return isAsc
                    ? query.OrderBy(x => x.TransactionType).ThenBy(x => x.Id)
                    : query.OrderByDescending(x => x.TransactionType).ThenByDescending(x => x.Id);

            case "referencetype":
                return isAsc
                    ? query.OrderBy(x => x.ReferenceType).ThenBy(x => x.Id)
                    : query.OrderByDescending(x => x.ReferenceType).ThenByDescending(x => x.Id);

            case "referenceid":
                return isAsc
                    ? query.OrderBy(x => x.ReferenceId).ThenBy(x => x.Id)
                    : query.OrderByDescending(x => x.ReferenceId).ThenByDescending(x => x.Id);

            case "afterqty":
            case "afterquantity":
                return isAsc
                    ? query.OrderBy(x => x.AfterQty).ThenBy(x => x.Id)
                    : query.OrderByDescending(x => x.AfterQty).ThenByDescending(x => x.Id);

            case "occurredatutc":
            case "transactiondate":
            default:
                return isAsc
                    ? query.OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.Id)
                    : query.OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id);
        }
    }
}