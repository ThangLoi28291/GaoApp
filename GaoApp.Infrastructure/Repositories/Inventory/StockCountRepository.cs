using GaoApp.Application.Common.Exceptions;
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
    private int? _lockedDocumentId;
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
        .ThenInclude(x => x.ProductVariant)
            .ThenInclude(x => x.Product)
                .ThenInclude(x => x.ProductImages)
                    .ThenInclude(x => x.MediaAsset)
    .FirstOrDefaultAsync(x => x.Id == id, ct);
    }


    public async Task<StockCountLine?> GetLineByIdAsync(int lineId, CancellationToken ct = default)
    {
        return await _context.StockCountLines
            .Include(x => x.StockCountDocument)
            .FirstOrDefaultAsync(x => x.Id == lineId, ct);
    }

    public Task<int?> GetDocumentIdForLineAsync(int lineId, CancellationToken ct = default)
        => _context.StockCountLines.AsNoTracking()
            .Where(x => x.Id == lineId)
            .Select(x => (int?)x.StockCountDocumentId)
            .SingleOrDefaultAsync(ct);

    public async Task LockDocumentForUpdateAsync(int documentId, CancellationToken ct = default)
    {
        var storeId = _context.CurrentStoreId ?? throw new TenantDataScopeException();
        if (_currentTransaction == null || _lockedDocumentId.HasValue)
            throw new InvalidOperationException("Kiểm kê cần một giao dịch riêng cho từng phiếu.");

        // Lock the tenant-scoped header first. Every mutation uses this same lock,
        // including inserts/deletes which would otherwise never check its RowVersion.
        var fresh = await _context.StockCountDocuments
            .FromSqlInterpolated($"SELECT * FROM [StockCountDocument] WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE [StoreId]={storeId} AND [Id]={documentId} AND [IsDeleted]=0")
            .AsNoTracking().SingleOrDefaultAsync(ct)
            ?? throw new BusinessRuleException("Phiếu kiểm kê không tồn tại.");

        var headers = _context.ChangeTracker.Entries<StockCountDocument>()
            .Where(x => x.Entity.Id == documentId).ToList();
        var lines = _context.ChangeTracker.Entries<StockCountLine>()
            .Where(x => x.Entity.StockCountDocumentId == documentId).ToList();
        foreach (var header in headers)
        {
            if (header.State != EntityState.Unchanged ||
                !header.Property(x => x.RowVersion).OriginalValue.SequenceEqual(fresh.RowVersion))
                throw new DbUpdateConcurrencyException("Phiếu kiểm kê đã thay đổi. Vui lòng tải lại.");
        }
        if (lines.Count > 0)
        {
            var versions = await _context.StockCountLines.AsNoTracking()
                .Where(x => x.StockCountDocumentId == documentId)
                .Select(x => new { x.Id, x.RowVersion }).ToDictionaryAsync(x => x.Id, ct);
            foreach (var line in lines)
            {
                if (line.State != EntityState.Unchanged ||
                    !versions.TryGetValue(line.Entity.Id, out var current) ||
                    !line.Property(x => x.RowVersion).OriginalValue.SequenceEqual(current.RowVersion))
                    throw new DbUpdateConcurrencyException("Dòng kiểm kê đã thay đổi. Vui lòng tải lại.");
            }
        }
        // A context can have read the graph earlier. Reload it only after the lock,
        // without silently discarding pending writes or an optimistic conflict.
        foreach (var line in lines) line.State = EntityState.Detached;
        foreach (var header in headers) header.State = EntityState.Detached;
        _lockedDocumentId = documentId;
    }

    public Task<bool> HasActiveLinesAsync(int documentId, CancellationToken ct = default)
        => _context.StockCountLines.AnyAsync(x => x.StockCountDocumentId == documentId, ct);

    public async Task<int> GetNextLineNoAsync(int stockCountDocumentId, CancellationToken ct = default)
    {
        var storeId = _context.CurrentStoreId ?? throw new TenantDataScopeException();
        var maxLineNo = await _context.StockCountLines
            .IgnoreQueryFilters()
            .Where(x => x.StoreId == storeId && x.StockCountDocumentId == stockCountDocumentId)
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
        if (_lockedDocumentId is int documentId &&
            _context.ChangeTracker.Entries<StockCountLine>().Any(x =>
                x.Entity.StockCountDocumentId == documentId &&
                x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var header = _context.ChangeTracker.Entries<StockCountDocument>()
                .Single(x => x.Entity.Id == documentId);
            // Advance the aggregate version even when only a child is written.
            header.Property(x => x.UpdatedAtUtc).CurrentValue = DateTime.UtcNow;
            header.Property(x => x.UpdatedAtUtc).IsModified = true;
        }
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
        if (_currentTransaction != null || _context.Database.CurrentTransaction != null)
            throw new InvalidOperationException("Kiểm kê cần một giao dịch riêng cho từng phiếu.");

        _currentTransaction = await _context.Database.BeginTransactionAsync(ct);
    }

    public async Task CommitTransactionAsync(CancellationToken ct = default)
    {
        if (_currentTransaction == null)
            return;

        await _currentTransaction.CommitAsync(ct);
        await _currentTransaction.DisposeAsync();
        _currentTransaction = null;
        _lockedDocumentId = null;
    }

    public async Task RollbackTransactionAsync(CancellationToken ct = default)
    {
        if (_currentTransaction == null)
            return;

        try { await _currentTransaction.RollbackAsync(ct); }
        finally
        {
            await _currentTransaction.DisposeAsync();
            _currentTransaction = null;
            _lockedDocumentId = null;
        }
    }

}
