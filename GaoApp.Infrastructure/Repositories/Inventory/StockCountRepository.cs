using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GaoApp.Infrastructure.Repositories.Inventory;

public class StockCountRepository : IStockCountRepository
{
    private readonly AppDbContext _context;
    private IDbContextTransaction? _currentTransaction;
    public StockCountRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(StockCountDocument entity, CancellationToken ct = default)
    {
        await _context.StockCountDocuments.AddAsync(entity, ct);
    }

    public async Task<StockCountDocument?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _context.StockCountDocuments
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<StockCountDocument?> GetDetailAsync(int id, CancellationToken ct = default)
    {
        return await _context.StockCountDocuments
            .Include(x => x.Warehouse)
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<StockCountLine?> GetLineByIdAsync(int lineId, CancellationToken ct = default)
    {
        return await _context.StockCountLines
            .Include(x => x.StockCountDocument)
            .FirstOrDefaultAsync(x => x.Id == lineId, ct);
    }

    public async Task<int> GetNextLineNoAsync(int stockCountDocumentId, CancellationToken ct = default)
    {
        var maxLineNo = await _context.StockCountLines
            .IgnoreQueryFilters()
            .Where(x => x.StockCountDocumentId == stockCountDocumentId)
            .Select(x => (int?)x.LineNo)
            .MaxAsync(ct);

        return (maxLineNo ?? 0) + 1;
    }

    public async Task<bool> WarehouseExistsAsync(int warehouseId, CancellationToken ct = default)
    {
        return await _context.Warehouses.AnyAsync(x => x.Id == warehouseId, ct);
    }

    public async Task<ProductVariant?> GetVariantForStockCountAsync(int productVariantId, CancellationToken ct = default)
    {
        return await _context.ProductVariants
            .Include(x => x.Product)
                .ThenInclude(x => x.BaseUnit)
            .FirstOrDefaultAsync(x => x.Id == productVariantId, ct);
    }

    public async Task<InventoryBalance?> GetInventoryBalanceAsync(int warehouseId, int productVariantId, CancellationToken ct = default)
    {
        return await _context.InventoryBalances
            .FirstOrDefaultAsync(x => x.WarehouseId == warehouseId && x.ProductVariantId == productVariantId, ct);
    }

    public async Task<ProductUnitConversion?> GetConversionAsync(int productVariantId, int unitId, CancellationToken ct = default)
    {
        return await _context.ProductUnitConversions
            .Include(x => x.Unit)
            .FirstOrDefaultAsync(x =>
                x.ProductVariantId == productVariantId &&
                x.UnitId == unitId &&
                x.IsActive, ct);
    }

    public async Task<ProductUnitConversion?> GetBaseConversionAsync(int productVariantId, CancellationToken ct = default)
    {
        return await _context.ProductUnitConversions
            .Include(x => x.Unit)
            .FirstOrDefaultAsync(x =>
                x.ProductVariantId == productVariantId &&
                x.IsBaseUnit &&
                x.IsActive, ct);
    }

    public Task RemoveLineAsync(StockCountLine line, CancellationToken ct = default)
    {
        _context.StockCountLines.Remove(line);
        return Task.CompletedTask;
    }

    public async Task<List<StockCountDocument>> GetListAsync(CancellationToken ct = default)
    {
        return await _context.StockCountDocuments
            .Include(x => x.Warehouse)
            .Include(x => x.Lines)
            .OrderByDescending(x => x.Id)
            .ToListAsync(ct);
    }

    public async Task<int> CountTodayAsync(DateTime localDate, CancellationToken ct = default)
    {
        return await _context.StockCountDocuments
            .CountAsync(x => x.DocumentDate.Date == localDate.Date, ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _context.SaveChangesAsync(ct);
    }
    public async Task<StockCountLine?> FindExistingEditableLineAsync(
    int stockCountDocumentId,
    int productVariantId,
    int unitId,
    CancellationToken ct = default)
    {
        return await _context.StockCountLines
            .FirstOrDefaultAsync(x =>
                x.StockCountDocumentId == stockCountDocumentId &&
                x.ProductVariantId == productVariantId &&
                x.UnitId == unitId, ct);
    }
    public async Task<string?> GetLastDocumentNoByDateAsync(DateTime localDate, CancellationToken ct = default)
    {
        var prefix = $"KK-{localDate:yyyyMMdd}-";

        return await _context.StockCountDocuments
            .Where(x => x.DocumentNo.StartsWith(prefix))
            .OrderByDescending(x => x.DocumentNo)
            .Select(x => x.DocumentNo)
            .FirstOrDefaultAsync(ct);
    }
    public async Task BeginTransactionAsync(CancellationToken ct = default)
    {
        if (_currentTransaction != null)
            return;

        _currentTransaction = await _context.Database.BeginTransactionAsync(ct);
    }

    public async Task CommitTransactionAsync(CancellationToken ct = default)
    {
        if (_currentTransaction == null)
            return;

        await _currentTransaction.CommitAsync(ct);
        await _currentTransaction.DisposeAsync();
        _currentTransaction = null;
    }

    public async Task RollbackTransactionAsync(CancellationToken ct = default)
    {
        if (_currentTransaction == null)
            return;

        await _currentTransaction.RollbackAsync(ct);
        await _currentTransaction.DisposeAsync();
        _currentTransaction = null;
    }
}