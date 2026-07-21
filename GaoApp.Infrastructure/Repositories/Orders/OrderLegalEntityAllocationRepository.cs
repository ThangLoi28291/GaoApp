using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Orders;

public sealed class OrderLegalEntityAllocationRepository
    : IOrderLegalEntityAllocationRepository
{
    private readonly AppDbContext _db;

    public OrderLegalEntityAllocationRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<Store?> GetStoreFeatureStateAsync(
        int storeId,
        CancellationToken ct = default)
        => _db.Stores
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == storeId && !x.IsDeleted, ct);

    public Task<List<LegalEntity>> GetActiveSalesLegalEntitiesAsync(
        int storeId,
        CancellationToken ct = default)
        => _db.LegalEntities
            .AsNoTracking()
            .Include(x => x.DefaultWarehouse)
            .Where(x => x.StoreId == storeId && x.IsActive && !x.IsDeleted)
            .OrderBy(x => x.SalePriority)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);

    public async Task<List<InventoryBalance>> LockInventoryForAllocationAsync(
        int storeId,
        IReadOnlyCollection<int> warehouseIds,
        IReadOnlyCollection<int> productVariantIds,
        CancellationToken ct = default)
    {
        if (storeId <= 0)
            throw new ArgumentOutOfRangeException(nameof(storeId));

        var keys = (warehouseIds ?? Array.Empty<int>())
            .Where(x => x > 0)
            .Distinct()
            .SelectMany(
                _ => (productVariantIds ?? Array.Empty<int>())
                    .Where(x => x > 0)
                    .Distinct(),
                (warehouseId, variantId) => (warehouseId, variantId))
            .OrderBy(x => x.warehouseId)
            .ThenBy(x => x.variantId)
            .ToList();

        if (keys.Count == 0)
            return new List<InventoryBalance>();

        // EF InMemory không hỗ trợ SQL hint; nhánh này chỉ phục vụ test model.
        if (!_db.Database.IsRelational())
        {
            var normalizedWarehouseIds = keys.Select(x => x.warehouseId).Distinct().ToList();
            var normalizedVariantIds = keys.Select(x => x.variantId).Distinct().ToList();

            return await _db.InventoryBalances
                .Where(x =>
                    x.StoreId == storeId &&
                    normalizedWarehouseIds.Contains(x.WarehouseId) &&
                    normalizedVariantIds.Contains(x.ProductVariantId) &&
                    !x.IsDeleted)
                .ToListAsync(ct);
        }

        var result = new List<InventoryBalance>();

        // SQL Server: UPDLOCK + HOLDLOCK giữ khóa đến hết transaction finalize.
        // Khóa từng key theo thứ tự cố định để giảm nguy cơ deadlock giữa hai POS.
        foreach (var key in keys)
        {
            var balance = await _db.InventoryBalances
                .FromSqlInterpolated($@"
                    SELECT TOP (1) *
                    FROM [InventoryBalances] WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
                    WHERE [StoreId] = {storeId}
                      AND [WarehouseId] = {key.warehouseId}
                      AND [ProductVariantId] = {key.variantId}
                      AND [IsDeleted] = 0")
                .SingleOrDefaultAsync(ct);

            if (balance != null)
                result.Add(balance);
        }

        return result;
    }

    public Task<bool> AnyForOrderAsync(int orderId, CancellationToken ct = default)
        => _db.OrderLegalEntityAllocations
            .AnyAsync(x => x.OrderId == orderId && !x.IsDeleted, ct);

    public Task<List<OrderLegalEntityAllocation>> GetForOrderAsync(
        int orderId,
        CancellationToken ct = default)
        => _db.OrderLegalEntityAllocations
            .AsNoTracking()
            .Where(x => x.OrderId == orderId && !x.IsDeleted)
            .OrderBy(x => x.OrderLineId)
            .ThenBy(x => x.SalePriority)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);

    public Task AddRangeAsync(
        IReadOnlyCollection<OrderLegalEntityAllocation> allocations,
        CancellationToken ct = default)
    {
        if (allocations == null || allocations.Count == 0)
            return Task.CompletedTask;

        return _db.OrderLegalEntityAllocations
            .AddRangeAsync(allocations, ct);
    }
}
