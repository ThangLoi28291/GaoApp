using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GaoApp.Infrastructure.Repositories.Inventory;

public class StockDocumentRepository : IStockDocumentRepository
{
    private readonly AppDbContext _context;
    private IDbContextTransaction? _transaction;

    public StockDocumentRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(StockDocument entity, CancellationToken ct = default)
    {
        await _context.StockDocuments.AddAsync(entity, ct);
    }

    public async Task<StockDocument?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _context.StockDocuments
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<StockDocument?> GetDetailAsync(int id, CancellationToken ct = default)
    {
        return await _context.StockDocuments
            .Include(x => x.Warehouse)
            .Include(x => x.Supplier)
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<StockDocument?> GetForConfirmAsync(int id, CancellationToken ct = default)
    {
        return await _context.StockDocuments
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<StockDocumentLine?> GetLineByIdAsync(int lineId, CancellationToken ct = default)
    {
        return await _context.StockDocumentLines
            .Include(x => x.StockDocument)
            .FirstOrDefaultAsync(x => x.Id == lineId, ct);
    }

    public async Task<int> GetNextLineNoAsync(int stockDocumentId, CancellationToken ct = default)
    {
        var maxLineNo = await _context.StockDocumentLines
            .IgnoreQueryFilters()
            .Where(x => x.StockDocumentId == stockDocumentId)
            .Select(x => (int?)x.LineNo)
            .MaxAsync(ct);

        return (maxLineNo ?? 0) + 1;
    }

    public async Task<bool> WarehouseExistsAsync(int warehouseId, CancellationToken ct = default)
    {
        return await _context.Warehouses.AnyAsync(x => x.Id == warehouseId, ct);
    }

    public async Task<bool> SupplierExistsAsync(int supplierId, CancellationToken ct = default)
    {
        return await _context.Suppliers.AnyAsync(x => x.Id == supplierId, ct);
    }

    public async Task<ProductVariant?> GetVariantForStockDocumentAsync(int productVariantId, CancellationToken ct = default)
    {
        return await _context.ProductVariants
            .Include(x => x.Product)
                .ThenInclude(x => x.BaseUnit)
            .FirstOrDefaultAsync(x => x.Id == productVariantId, ct);
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

    public async Task<InventoryBalance?> GetInventoryBalanceAsync(int warehouseId, int productVariantId, CancellationToken ct = default)
    {
        return await _context.InventoryBalances
            .FirstOrDefaultAsync(x => x.WarehouseId == warehouseId && x.ProductVariantId == productVariantId, ct);
    }

    public async Task AddInventoryBalanceAsync(InventoryBalance entity, CancellationToken ct = default)
    {
        await _context.InventoryBalances.AddAsync(entity, ct);
    }

    public async Task AddInventoryTransactionAsync(InventoryTransaction entity, CancellationToken ct = default)
    {
        await _context.InventoryTransactions.AddAsync(entity, ct);
    }

    

    public async Task<List<StockDocument>> GetReceiptListAsync(CancellationToken ct = default)
    {
        return await _context.StockDocuments
            .Include(x => x.Warehouse)
            .Include(x => x.Supplier)
            .Where(x => x.Type == StockDocumentType.Receipt)
            .OrderByDescending(x => x.Id)
            .ToListAsync(ct);
    }

    public Task RemoveLineAsync(StockDocumentLine line, CancellationToken ct = default)
    {
        _context.StockDocumentLines.Remove(line);
        return Task.CompletedTask;
    }

    public async Task<bool> ExistsInventoryTransactionByReferenceLineAsync(
        InventoryReferenceType referenceType,
        string referenceId,
        int referenceLineId,
        CancellationToken ct = default)
    {
        return await _context.InventoryTransactions.AnyAsync(x =>
            x.ReferenceType == referenceType &&
            x.ReferenceId == referenceId &&
            x.ReferenceLineId == referenceLineId, ct);
    }

    public async Task BeginTransactionAsync(CancellationToken ct = default)
    {
        _transaction = await _context.Database.BeginTransactionAsync(ct);
    }

    public async Task CommitTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction != null)
        {
            await _transaction.CommitAsync(ct);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction != null)
        {
            await _transaction.RollbackAsync(ct);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _context.SaveChangesAsync(ct);
    }
   
}