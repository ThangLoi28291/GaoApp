using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Inventory;

public sealed class InventoryBalanceRepository : IInventoryBalanceRepository
{
    private readonly AppDbContext _db;

    public InventoryBalanceRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<InventoryBalance?> GetByWarehouseAndVariantAsync(
        int warehouseId,
        int productVariantId,
        CancellationToken ct = default)
    {
        return await _db.InventoryBalances
            .FirstOrDefaultAsync(x =>
                x.WarehouseId == warehouseId &&
                x.ProductVariantId == productVariantId, ct);
    }

    public async Task<InventoryBalance> GetOrCreateAsync(int warehouseId, int productVariantId, CancellationToken ct = default)
    {
        var entity = await _db.InventoryBalances
            .FirstOrDefaultAsync(x =>
                x.WarehouseId == warehouseId &&
                x.ProductVariantId == productVariantId &&
                !x.IsDeleted, ct);

        if (entity != null)
            return entity;

        entity = new InventoryBalance
        {
            WarehouseId = warehouseId,
            ProductVariantId = productVariantId,
            OnHandQty = 0,
            ReservedQty = 0,
            InventoryValue = 0,
            AverageUnitCost = 0
        };

        await _db.InventoryBalances.AddAsync(entity, ct);
        return entity;
    }

    public Task AddAsync(InventoryBalance balance, CancellationToken ct = default)
        => _db.InventoryBalances.AddAsync(balance, ct).AsTask();

    public async Task<List<InventoryBalance>> GetByVariantAsync(int productVariantId, CancellationToken ct = default)
    {
        return await _db.InventoryBalances
            .Where(x => x.ProductVariantId == productVariantId)
            .OrderBy(x => x.WarehouseId)
            .ToListAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);

    public async Task<List<InventoryBalance>> GetNegativeBalancesAsync(CancellationToken ct = default)
    {
        return await _db.InventoryBalances
            .Include(x => x.Warehouse)
            .Include(x => x.ProductVariant)
                .ThenInclude(x => x.Product)
            .Where(x => x.OnHandQty < 0)
            .OrderBy(x => x.WarehouseId)
            .ThenBy(x => x.ProductVariantId)
            .ToListAsync(ct);
    }

    public async Task<InventoryBalance?> GetDetailByWarehouseAndVariantAsync(
        int warehouseId,
        int productVariantId,
        CancellationToken ct = default)
    {
        return await _db.InventoryBalances
            .Include(x => x.Warehouse)
            .Include(x => x.ProductVariant)
                .ThenInclude(x => x.Product)
            .FirstOrDefaultAsync(x =>
                x.WarehouseId == warehouseId &&
                x.ProductVariantId == productVariantId, ct);
    }

    /// <summary>
    /// Query tồn kho hiện tại.
    /// Hỗ trợ:
    /// - lọc theo kho
    /// - lọc theo variant
    /// - lọc theo keyword
    /// - chỉ lấy dòng âm
    /// - chỉ lấy dòng dương
    /// - sort
    /// - phân trang
    ///
    /// Ghi chú barcode:
    /// - Không còn dùng ProductVariant.Barcode
    /// - Search barcode đi qua UnitConversions -> Barcodes
    /// </summary>
    public async Task<(List<InventoryBalance> Items, int TotalItems)> QueryCurrentBalancesAsync(
        InventoryBalanceQueryRequest request,
        CancellationToken ct = default)
    {
        var page = request.Page <= 0 ? 1 : request.Page;
        var pageSize = request.PageSize <= 0 ? 20 : request.PageSize;

        var keyword = string.IsNullOrWhiteSpace(request.Keyword)
            ? null
            : request.Keyword.Trim();

        var query = _db.InventoryBalances
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.ProductVariant)
                .ThenInclude(x => x.Product)
            // Include đủ để service resolve barcode đại diện
            .Include(x => x.ProductVariant)
                .ThenInclude(x => x.UnitConversions.Where(c => !c.IsDeleted && c.IsActive))
                    .ThenInclude(c => c.Barcodes.Where(b => !b.IsDeleted && b.IsActive))
            .AsQueryable();

        // 1. Lọc theo kho
        if (request.WarehouseId.HasValue && request.WarehouseId.Value > 0)
        {
            query = query.Where(x => x.WarehouseId == request.WarehouseId.Value);
        }

        // 2. Lọc theo variant
        if (request.ProductVariantId.HasValue && request.ProductVariantId.Value > 0)
        {
            query = query.Where(x => x.ProductVariantId == request.ProductVariantId.Value);
        }

        // 3. Chỉ lấy hàng âm
        if (request.OnlyNegative)
        {
            query = query.Where(x => x.OnHandQty < 0);
        }

        // 4. Chỉ lấy hàng còn tồn dương
        if (request.OnlyPositive)
        {
            query = query.Where(x => x.OnHandQty > 0);
        }

        // 5. Tìm theo keyword
        // Hỗ trợ:
        // - tên sản phẩm
        // - SKU
        // - barcode đơn vị
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
                        b.Barcode.Contains(keyword))));
        }

        // 6. Sort
        query = ApplyBalanceSort(query, request.SortBy, request.SortDirection);

        var totalItems = await query.CountAsync(ct);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalItems);
    }

    public async Task<Dictionary<int, decimal>> GetAvailableQtyMapByVariantIdsAsync(
    int storeId,
    int warehouseId,
    IReadOnlyCollection<int> variantIds,
    CancellationToken ct = default)
    {
        if (storeId <= 0 || warehouseId <= 0 || variantIds == null || variantIds.Count == 0)
        {
            return new Dictionary<int, decimal>();
        }

        var normalizedVariantIds = variantIds
            .Where(x => x > 0)
            .Distinct()
            .ToList();

        if (normalizedVariantIds.Count == 0)
        {
            return new Dictionary<int, decimal>();
        }

        var items = await _db.InventoryBalances
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted &&
                x.StoreId == storeId &&
                x.WarehouseId == warehouseId &&
                normalizedVariantIds.Contains(x.ProductVariantId))
            .Select(x => new
            {
                x.ProductVariantId,
                AvailableQty = x.OnHandQty - x.ReservedQty
            })
            .ToListAsync(ct);

        return items.ToDictionary(
            x => x.ProductVariantId,
            x => x.AvailableQty);
    }
    /// <summary>
    /// Áp dụng sort cho query tồn kho.
    /// </summary>
    private static IQueryable<InventoryBalance> ApplyBalanceSort(
        IQueryable<InventoryBalance> query,
        string? sortBy,
        string? sortDirection)
    {
        var normalizedSortBy = string.IsNullOrWhiteSpace(sortBy)
            ? "ProductName"
            : sortBy.Trim();

        var normalizedSortDirection = string.IsNullOrWhiteSpace(sortDirection)
            ? "asc"
            : sortDirection.Trim().ToLowerInvariant();

        var isAsc = normalizedSortDirection == "asc";

        switch (normalizedSortBy.Trim().ToLowerInvariant())
        {
            case "warehouse":
                return isAsc
                    ? query.OrderBy(x => x.Warehouse.Name).ThenBy(x => x.ProductVariantId)
                    : query.OrderByDescending(x => x.Warehouse.Name).ThenByDescending(x => x.ProductVariantId);

            case "sku":
                return isAsc
                    ? query.OrderBy(x => x.ProductVariant.Sku).ThenBy(x => x.ProductVariantId)
                    : query.OrderByDescending(x => x.ProductVariant.Sku).ThenByDescending(x => x.ProductVariantId);

            case "onhandqty":
                return isAsc
                    ? query.OrderBy(x => x.OnHandQty).ThenBy(x => x.ProductVariantId)
                    : query.OrderByDescending(x => x.OnHandQty).ThenByDescending(x => x.ProductVariantId);

            case "reservedqty":
                return isAsc
                    ? query.OrderBy(x => x.ReservedQty).ThenBy(x => x.ProductVariantId)
                    : query.OrderByDescending(x => x.ReservedQty).ThenByDescending(x => x.ProductVariantId);

            case "availableqty":
                return isAsc
                    ? query.OrderBy(x => x.AvailableQty).ThenBy(x => x.ProductVariantId)
                    : query.OrderByDescending(x => x.AvailableQty).ThenByDescending(x => x.ProductVariantId);

            case "productname":
            default:
                return isAsc
                    ? query.OrderBy(x => x.ProductVariant.Product!.Name).ThenBy(x => x.ProductVariant.Sku)
                    : query.OrderByDescending(x => x.ProductVariant.Product!.Name).ThenByDescending(x => x.ProductVariant.Sku);
        }
    }
}