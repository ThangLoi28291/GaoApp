using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GaoApp.Infrastructure.Repositories.Inventory;

/// <summary>
/// Repository triển khai dữ liệu cho phiếu chuyển kho.
/// </summary>
public class StockTransferRepository : IStockTransferRepository
{
    private readonly AppDbContext _context;
    private IDbContextTransaction? _currentTransaction;
    private int? _lockedDocumentId;

    public StockTransferRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<(List<StockTransferDocument> Items, int Total)> GetListAsync(
        int page,
        int pageSize,
        string? keyword,
        int? fromWarehouseId,
        int? toWarehouseId,
        int? status,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken ct = default)
    {
        var query = _context.StockTransferDocuments
            .AsNoTracking()
            .Include(x => x.FromWarehouse)
            .Include(x => x.ToWarehouse)
            .Include(x => x.Lines)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            keyword = keyword.Trim();
            query = query.Where(x =>
                x.DocumentNo.Contains(keyword) ||
                (x.Note != null && x.Note.Contains(keyword)));
        }

        if (fromWarehouseId.HasValue)
            query = query.Where(x => x.FromWarehouseId == fromWarehouseId.Value);

        if (toWarehouseId.HasValue)
            query = query.Where(x => x.ToWarehouseId == toWarehouseId.Value);

        if (status.HasValue)
            query = query.Where(x => (int)x.Status == status.Value);

        if (fromDate.HasValue)
        {
            var from = fromDate.Value.Date;
            query = query.Where(x => x.DocumentDate >= from);
        }

        if (toDate.HasValue)
        {
            var toExclusive = toDate.Value.Date.AddDays(1);
            query = query.Where(x => x.DocumentDate < toExclusive);
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(x => x.DocumentDate)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task AddAsync(StockTransferDocument document, CancellationToken ct = default)
    {
        await _context.StockTransferDocuments.AddAsync(document, ct);
    }

    public async Task<StockTransferDocument?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _context.StockTransferDocuments
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<StockTransferDocument?> GetDetailAsync(int id, CancellationToken ct = default)
    {
        return await _context.StockTransferDocuments
            .Include(x => x.FromWarehouse)
            .Include(x => x.ToWarehouse)
            .Include(x => x.Lines)
                .ThenInclude(x => x.ProductVariant)
                    .ThenInclude(x => x.Product)
                        .ThenInclude(x => x.ProductImages)
                            .ThenInclude(x => x.MediaAsset)
            .Include(x => x.Lines)
                .ThenInclude(x => x.ProductVariant)
                    .ThenInclude(x => x.UnitConversions)
                        .ThenInclude(x => x.Barcodes)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<StockTransferLine?> GetLineByIdAsync(int lineId, CancellationToken ct = default)
    {
        return await _context.StockTransferLines
            .Include(x => x.StockTransferDocument)
            .Include(x => x.ProductVariant)
                .ThenInclude(x => x.Product)
                    .ThenInclude(x => x.ProductImages)
                        .ThenInclude(x => x.MediaAsset)
            .Include(x => x.ProductVariant)
                .ThenInclude(x => x.UnitConversions)
                    .ThenInclude(x => x.Barcodes)
            .FirstOrDefaultAsync(x => x.Id == lineId, ct);
    }

    public Task RemoveLineAsync(StockTransferLine line, CancellationToken ct = default)
    {
        _context.StockTransferLines.Remove(line);
        return Task.CompletedTask;
    }

    public Task<int?> GetDocumentIdForLineAsync(int lineId, CancellationToken ct = default)
        => _context.StockTransferLines.AsNoTracking()
            .Where(x => x.Id == lineId)
            .Select(x => (int?)x.StockTransferDocumentId)
            .SingleOrDefaultAsync(ct);

    public async Task LockDocumentForUpdateAsync(int documentId, CancellationToken ct = default)
    {
        var storeId = _context.CurrentStoreId ?? throw new TenantDataScopeException();
        if (_currentTransaction == null || _lockedDocumentId.HasValue)
            throw new InvalidOperationException("Chuyển kho cần một giao dịch riêng cho từng phiếu.");

        // Lock the tenant-scoped header first. Every mutation uses this same lock,
        // including inserts/deletes which would otherwise never check its RowVersion.
        var fresh = await _context.StockTransferDocuments
            .FromSqlInterpolated($"SELECT * FROM [StockTransferDocument] WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE [StoreId]={storeId} AND [Id]={documentId} AND [IsDeleted]=0")
            .AsNoTracking().SingleOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Phiếu chuyển kho không tồn tại.");

        var headers = _context.ChangeTracker.Entries<StockTransferDocument>()
            .Where(x => x.Entity.Id == documentId).ToList();
        var lines = _context.ChangeTracker.Entries<StockTransferLine>()
            .Where(x => x.Entity.StockTransferDocumentId == documentId).ToList();
        foreach (var header in headers)
        {
            if (header.State != EntityState.Unchanged ||
                !header.Property(x => x.RowVersion).OriginalValue.SequenceEqual(fresh.RowVersion))
                throw new DbUpdateConcurrencyException("Phiếu chuyển kho đã thay đổi. Vui lòng tải lại.");
        }
        if (lines.Count > 0)
        {
            var versions = await _context.StockTransferLines.AsNoTracking()
                .Where(x => x.StockTransferDocumentId == documentId)
                .Select(x => new { x.Id, x.RowVersion }).ToDictionaryAsync(x => x.Id, ct);
            foreach (var line in lines)
            {
                if (line.State != EntityState.Unchanged ||
                    !versions.TryGetValue(line.Entity.Id, out var current) ||
                    !line.Property(x => x.RowVersion).OriginalValue.SequenceEqual(current.RowVersion))
                    throw new DbUpdateConcurrencyException("Dòng chuyển kho đã thay đổi. Vui lòng tải lại.");
            }
        }
        // A context can have read the graph earlier. Reload it only after the lock,
        // without silently discarding pending writes or an optimistic conflict.
        foreach (var line in lines) line.State = EntityState.Detached;
        foreach (var header in headers) header.State = EntityState.Detached;
        _lockedDocumentId = documentId;
    }

    public async Task<int> GetNextLineNoAsync(int documentId, CancellationToken ct = default)
    {
        var storeId = _context.CurrentStoreId ?? throw new TenantDataScopeException();
        var maxLineNo = await _context.StockTransferLines
            .IgnoreQueryFilters()
            .Where(x => x.StoreId == storeId && x.StockTransferDocumentId == documentId)
            .Select(x => (int?)x.LineNo)
            .MaxAsync(ct) ?? 0;

        return maxLineNo + 1;
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        if (_lockedDocumentId is int documentId &&
            _context.ChangeTracker.Entries<StockTransferLine>().Any(x =>
                x.Entity.StockTransferDocumentId == documentId &&
                x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var header = _context.ChangeTracker.Entries<StockTransferDocument>()
                .Single(x => x.Entity.Id == documentId);
            // Advance the aggregate version even when only a child is written.
            header.Property(x => x.UpdatedAtUtc).CurrentValue = DateTime.UtcNow;
            header.Property(x => x.UpdatedAtUtc).IsModified = true;
        }
        await _context.SaveChangesAsync(ct);
    }

    public async Task BeginTransactionAsync(CancellationToken ct = default)
    {
        if (_currentTransaction != null || _context.Database.CurrentTransaction != null)
            throw new InvalidOperationException("Chuyển kho cần một giao dịch riêng cho từng phiếu.");

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

    public async Task<bool> WarehouseExistsAsync(int warehouseId, CancellationToken ct = default)
    {
        return await _context.Warehouses.AnyAsync(x => x.Id == warehouseId, ct);
    }

    public async Task<Warehouse?> GetWarehouseByIdAsync(int warehouseId, CancellationToken ct = default)
    {
        return await _context.Warehouses
            .FirstOrDefaultAsync(x => x.Id == warehouseId, ct);
    }

    public async Task<ProductVariant?> GetVariantForTransferAsync(int productVariantId, CancellationToken ct = default)
    {
        return await _context.ProductVariants
            .Include(x => x.Product)
                .ThenInclude(x => x.ProductImages)
                    .ThenInclude(x => x.MediaAsset)
            .Include(x => x.UnitConversions)
                .ThenInclude(x => x.Barcodes)
            .FirstOrDefaultAsync(x => x.Id == productVariantId, ct);
    }

    public async Task<string?> GetLastDocumentNoByDateAsync(DateTime documentDate, CancellationToken ct = default)
    {
        var prefix = $"CK-{documentDate:yyyyMMdd}-";

        return await _context.StockTransferDocuments
            .Where(x => x.DocumentNo.StartsWith(prefix))
            .OrderByDescending(x => x.DocumentNo)
            .Select(x => x.DocumentNo)
            .FirstOrDefaultAsync(ct);
    }
}
