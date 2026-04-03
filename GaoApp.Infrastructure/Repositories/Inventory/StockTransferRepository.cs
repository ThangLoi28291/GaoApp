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
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<StockTransferLine?> GetLineByIdAsync(int lineId, CancellationToken ct = default)
    {
        return await _context.StockTransferLines
            .Include(x => x.StockTransferDocument)
            .FirstOrDefaultAsync(x => x.Id == lineId, ct);
    }

    public Task RemoveLineAsync(StockTransferLine line, CancellationToken ct = default)
    {
        _context.StockTransferLines.Remove(line);
        return Task.CompletedTask;
    }

    public async Task<int> GetNextLineNoAsync(int documentId, CancellationToken ct = default)
    {
        var maxLineNo = await _context.StockTransferLines
            .IgnoreQueryFilters()
            .Where(x => x.StockTransferDocumentId == documentId)
            .Select(x => (int?)x.LineNo)
            .MaxAsync(ct) ?? 0;

        return maxLineNo + 1;
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _context.SaveChangesAsync(ct);
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