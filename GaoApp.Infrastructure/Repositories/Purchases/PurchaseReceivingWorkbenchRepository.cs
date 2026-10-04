using GaoApp.Application.Interfaces.Common;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Application.Interfaces.Repositories.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GaoApp.Infrastructure.Repositories.Purchases;

public sealed class PurchaseReceivingWorkbenchRepository : IPurchaseReceivingWorkbenchRepository
{
    private readonly AppDbContext _context;
    private IDbContextTransaction? _transaction;

    private readonly ICurrentPOSContext? _pos;
    public PurchaseReceivingWorkbenchRepository(AppDbContext context, ICurrentPOSContext? pos = null)
    {
        _context = context;
        _pos = pos;
    }

    public async Task BeginTransactionAsync(CancellationToken ct = default)
        => _transaction = await _context.Database.BeginTransactionAsync(ct);

    public async Task CommitTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction is null) return;
        await _transaction.CommitAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task RollbackTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction is null) return;
        await _transaction.RollbackAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task<PurchaseOrder?> LockAndGetPurchaseOrderAsync(
        int storeId, int purchaseOrderId, CancellationToken ct = default)
    {
        EnsureTransaction();
        var locked = await _context.PurchaseOrders
            .FromSqlInterpolated($@"SELECT po.* FROM [PurchaseOrders] po WITH (UPDLOCK,HOLDLOCK,ROWLOCK)
                WHERE po.[Id] = {purchaseOrderId} AND po.[StoreId] = {storeId} AND po.[IsDeleted] = 0")
            .AsNoTracking()
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(ct);
        if (!locked.HasValue) return null;
        return await PurchaseOrderQuery().SingleAsync(x => x.Id == purchaseOrderId, ct);
    }

    public Task<StockDocument?> GetEditableForPurchaseOrderAsync(
        int storeId, int purchaseOrderId, CancellationToken ct = default)
        => DocumentQuery(tracking: true).FirstOrDefaultAsync(x =>
            x.StoreId == storeId && x.PurchaseOrderId == purchaseOrderId &&
            x.Type == StockDocumentType.Receipt &&
            x.ReceiptSource == PurchaseReceiptSource.PurchaseOrder &&
            (x.Status == StockDocumentStatus.Draft ||
             x.Status == StockDocumentStatus.Rejected) && !x.IsDeleted, ct);

    public async Task<StockDocument?> LockAndGetDocumentAsync(
        int storeId, int stockDocumentId, CancellationToken ct = default)
    {
        EnsureTransaction();
        var locked = await _context.StockDocuments
            .FromSqlInterpolated($@"SELECT d.* FROM [StockDocument] d WITH (UPDLOCK,HOLDLOCK,ROWLOCK)
                WHERE d.[Id] = {stockDocumentId} AND d.[StoreId] = {storeId} AND d.[IsDeleted] = 0")
            .AsNoTracking()
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(ct);
        if (!locked.HasValue) return null;
        return await DocumentQuery(tracking: true).SingleAsync(x => x.Id == stockDocumentId, ct);
    }

    public Task<StockDocument?> GetDocumentAsync(
        int storeId, int stockDocumentId, bool tracking, CancellationToken ct = default)
        => DocumentQuery(tracking).SingleOrDefaultAsync(x =>
            x.Id == stockDocumentId && x.StoreId == storeId && !x.IsDeleted, ct);

    public Task<ProductUnitConversion?> GetConversionAsync(
        int storeId, int conversionId, CancellationToken ct = default)
        => _context.ProductUnitConversions
            .Include(x => x.Unit)
            .Include(x => x.ProductVariant).ThenInclude(x => x.Product)
            .SingleOrDefaultAsync(x => x.Id == conversionId && x.StoreId == storeId &&
                !x.IsDeleted && x.IsActive && !x.Unit.IsDeleted && x.Unit.IsActive &&
                !x.ProductVariant.IsDeleted && x.ProductVariant.IsActive &&
                !x.ProductVariant.Product.IsDeleted && x.ProductVariant.Product.IsActive, ct);

    public Task<PurchaseReceivingAction?> GetCommandAsync(
        int storeId, int stockDocumentId, Guid commandId, CancellationToken ct = default)
        => _context.PurchaseReceivingActions.SingleOrDefaultAsync(x =>
            x.StoreId == storeId && x.StockDocumentId == stockDocumentId && x.CommandId == commandId, ct);

    public Task<PurchaseReceivingAction?> GetLatestUndoableAsync(
        int storeId, int stockDocumentId, int revision, CancellationToken ct = default)
        => _context.PurchaseReceivingActions
            .Where(x => x.StoreId == storeId && x.StockDocumentId == stockDocumentId &&
                x.ReceivingRevision == revision && x.ActionType != PurchaseReceivingActionType.Undo &&
                x.UndoOfActionId == null &&
                !_context.PurchaseReceivingActions.Any(u => u.UndoOfActionId == x.Id))
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

    public async Task AddDocumentAsync(StockDocument document, CancellationToken ct = default)
    {
        await ReceiptEntryTerminal.CaptureAsync(_context, _pos, document, ct);
        await _context.StockDocuments.AddAsync(document, ct);
    }

    public Task AddActionAsync(PurchaseReceivingAction action, CancellationToken ct = default)
        => _context.PurchaseReceivingActions.AddAsync(action, ct).AsTask();

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _context.SaveChangesAsync(ct);

    private IQueryable<PurchaseOrder> PurchaseOrderQuery()
        => _context.PurchaseOrders
            .Include(x => x.Supplier)
            .Include(x => x.ExpectedWarehouse).ThenInclude(x => x.LegalEntity)
            .Include(x => x.Lines).ThenInclude(x => x.ProductVariant).ThenInclude(x => x!.Product).ThenInclude(x => x.BaseUnit)
            .Include(x => x.Lines).ThenInclude(x => x.ProductUnitConversion).ThenInclude(x => x!.Unit)
            .Include(x => x.Lines).ThenInclude(x => x.Unit)
            .AsSplitQuery();

    private IQueryable<StockDocument> DocumentQuery(bool tracking)
    {
        var query = _context.StockDocuments
            .Include(x => x.Warehouse).ThenInclude(x => x.LegalEntity)
            .Include(x => x.Supplier)
            .Include(x => x.PurchaseOrder).ThenInclude(x => x!.Lines).ThenInclude(x => x.ProductVariant).ThenInclude(x => x!.Product).ThenInclude(x => x.BaseUnit)
            .Include(x => x.Lines).ThenInclude(x => x.ProductVariant).ThenInclude(x => x.Product).ThenInclude(x => x.BaseUnit)
            .Include(x => x.Lines).ThenInclude(x => x.ProductUnitConversion).ThenInclude(x => x!.Unit)
            .Include(x => x.Lines).ThenInclude(x => x.Unit)
            .Include(x => x.ProvisionalItems)
            .AsSplitQuery();
        return tracking ? query : query.AsNoTracking();
    }

    private void EnsureTransaction()
    {
        if (_transaction is null)
            throw new InvalidOperationException("Receiving Workbench lock requires an active transaction.");
    }
}
