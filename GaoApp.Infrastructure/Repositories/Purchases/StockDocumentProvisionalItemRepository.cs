using GaoApp.Application.Interfaces.Repositories.Purchases;
using GaoApp.Application.DTOs.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Purchases;

public sealed class StockDocumentProvisionalItemRepository
    : IStockDocumentProvisionalItemRepository
{
    private readonly AppDbContext _db;

    public StockDocumentProvisionalItemRepository(AppDbContext db) => _db = db;

    public async Task<StockDocument?> LockAndGetDocumentAsync(
        int storeId, int stockDocumentId, CancellationToken ct = default)
    {
        var locked = await _db.StockDocuments
            .FromSqlInterpolated($@"SELECT d.* FROM [StockDocument] d WITH (UPDLOCK,HOLDLOCK,ROWLOCK)
                WHERE d.[Id] = {stockDocumentId} AND d.[StoreId] = {storeId} AND d.[IsDeleted] = 0")
            .AsNoTracking()
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(ct);
        return locked.HasValue ? await DocumentQuery(tracking: true)
            .SingleAsync(x => x.Id == stockDocumentId && x.StoreId == storeId, ct) : null;
    }

    public Task<StockDocument?> GetDocumentAsync(
        int storeId, int stockDocumentId, CancellationToken ct = default)
        => DocumentQuery(tracking: false).SingleOrDefaultAsync(x =>
            x.Id == stockDocumentId && x.StoreId == storeId, ct);

    public Task<StockDocumentProvisionalItem?> GetItemAsync(
        int storeId, int stockDocumentId, int itemId, CancellationToken ct = default)
        => _db.StockDocumentProvisionalItems.SingleOrDefaultAsync(x =>
            x.Id == itemId && x.StockDocumentId == stockDocumentId && x.StoreId == storeId, ct);

    public Task<StockDocumentProvisionalItem?> FindAccumulationTargetAsync(
        int storeId, int stockDocumentId, string normalizedBarcode, int? unitId,
        string? normalizedUnitName, CancellationToken ct = default)
        => _db.StockDocumentProvisionalItems.SingleOrDefaultAsync(x =>
            x.StoreId == storeId && x.StockDocumentId == stockDocumentId &&
            x.Status == StockDocumentProvisionalItemStatus.Unresolved &&
            x.NormalizedBarcode == normalizedBarcode &&
            (unitId.HasValue
                ? x.UnitId == unitId
                : x.UnitId == null && normalizedUnitName != null &&
                  x.NormalizedUnitNameSnapshot == normalizedUnitName), ct);

    public Task<ProductUnitConversion?> GetConversionAsync(
        int storeId, int variantId, int conversionId, CancellationToken ct = default)
        => _db.ProductUnitConversions
            .Include(x => x.Unit)
            .Include(x => x.ProductVariant).ThenInclude(x => x.Product)
            .SingleOrDefaultAsync(x => x.Id == conversionId &&
                x.ProductVariantId == variantId && x.StoreId == storeId &&
                x.IsActive && !x.IsDeleted && x.Factor > 0m &&
                x.Unit.StoreId == storeId && x.Unit.IsActive && !x.Unit.IsDeleted &&
                x.ProductVariant.StoreId == storeId && x.ProductVariant.IsActive &&
                !x.ProductVariant.IsDeleted && x.ProductVariant.Product.StoreId == storeId &&
                x.ProductVariant.Product.IsActive && !x.ProductVariant.Product.IsDeleted, ct);

    public Task<Unit?> GetUnitAsync(int storeId, int unitId, CancellationToken ct = default)
        => _db.Units.SingleOrDefaultAsync(x => x.Id == unitId && x.StoreId == storeId &&
            x.IsActive && !x.IsDeleted, ct);

    public async Task<IReadOnlyList<ProvisionalBarcodeEvidence>> GetBarcodeEvidenceAsync(
        int storeId, string normalizedBarcode, CancellationToken ct = default)
    {
        var current = await _db.ProductVariantUnitBarcodes.IgnoreQueryFilters()
            .Where(x => x.StoreId == storeId && x.Barcode == normalizedBarcode)
            .Select(x => new ProvisionalBarcodeEvidence(
                x.Id, x.ProductUnitConversionId, x.IsActive && !x.IsDeleted, false))
            .ToListAsync(ct);
        var history = await _db.ProductVariantBarcodeHistories.IgnoreQueryFilters()
            .Where(x => x.StoreId == storeId &&
                (x.OldBarcode == normalizedBarcode || x.NewBarcode == normalizedBarcode))
            .Select(x => new ProvisionalBarcodeEvidence(
                null, x.ProductUnitConversionId, false, true))
            .ToListAsync(ct);
        return current.Concat(history).ToList();
    }

    public Task<PurchaseReceivingAction?> GetCommandAsync(
        int storeId, int stockDocumentId, Guid commandId, CancellationToken ct = default)
        => _db.PurchaseReceivingActions.SingleOrDefaultAsync(x =>
            x.StoreId == storeId && x.StockDocumentId == stockDocumentId &&
            x.CommandId == commandId, ct);

    public Task<PurchaseReceivingAction?> GetLatestProvisionalUndoableAsync(
        int storeId, int stockDocumentId, int revision, CancellationToken ct = default)
        => _db.PurchaseReceivingActions
            .Where(x => x.StoreId == storeId && x.StockDocumentId == stockDocumentId &&
                x.ReceivingRevision == revision && x.StockDocumentProvisionalItemId != null &&
                (x.ActionType == PurchaseReceivingActionType.ProvisionalCapture ||
                 x.ActionType == PurchaseReceivingActionType.ProvisionalAccumulate ||
                 x.ActionType == PurchaseReceivingActionType.ProvisionalEdit ||
                 x.ActionType == PurchaseReceivingActionType.ProvisionalRemove) &&
                x.UndoOfActionId == null &&
                !_db.PurchaseReceivingActions.Any(u => u.UndoOfActionId == x.Id))
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ReceiptIntakeRecentDto>> GetRecentReceiptsAsync(
        int storeId, int stockDocumentId, CancellationToken ct = default)
        => await _db.PurchaseReceivingActions.AsNoTracking()
            .Where(x => x.StoreId == storeId && x.StockDocumentId == stockDocumentId &&
                x.StockDocumentProvisionalItemId != null && x.UndoOfActionId == null &&
                !_db.PurchaseReceivingActions.Any(u => u.StoreId == storeId && u.StockDocumentId == stockDocumentId && u.UndoOfActionId == x.Id) &&
                (x.ActionType == PurchaseReceivingActionType.ProvisionalCapture ||
                 x.ActionType == PurchaseReceivingActionType.ProvisionalAccumulate ||
                 ((x.ActionType == PurchaseReceivingActionType.ProvisionalLinkExisting ||
                   x.ActionType == PurchaseReceivingActionType.ProvisionalQuickCreate) &&
                  x.AfterQuantity > (_db.PurchaseReceivingActions.Where(c => c.StoreId == storeId && c.StockDocumentId == stockDocumentId &&
                    c.StockDocumentProvisionalItemId == x.StockDocumentProvisionalItemId && c.Id < x.Id)
                    .OrderByDescending(c => c.Id).Select(c => (decimal?)c.AfterQuantity).FirstOrDefault() ?? 0))))
            .OrderByDescending(x => x.Id).Take(10)
            .Select(x => new ReceiptIntakeRecentDto(x.CommandId,
                x.StockDocumentProvisionalItem!.ResolvedStockDocumentLineId.HasValue
                    ? "line-" + x.StockDocumentProvisionalItem.ResolvedStockDocumentLineId : "intake-" + x.StockDocumentProvisionalItemId,
                x.StockDocumentProvisionalItem.ResolvedStockDocumentLine != null
                    ? x.StockDocumentProvisionalItem.ResolvedStockDocumentLine.ProductNameSnapshot : x.StockDocumentProvisionalItem.NameSnapshot,
                x.StockDocumentProvisionalItem.RawBarcodeSnapshot,
                x.StockDocumentProvisionalItem.ResolvedStockDocumentLine != null
                    ? x.StockDocumentProvisionalItem.ResolvedStockDocumentLine.UnitNameSnapshot ?? "" : x.StockDocumentProvisionalItem.UnitNameSnapshot ?? "",
                x.ActionType == PurchaseReceivingActionType.ProvisionalCapture ? x.AfterQuantity :
                x.ActionType == PurchaseReceivingActionType.ProvisionalAccumulate ? x.AfterQuantity - x.BeforeQuantity :
                    x.AfterQuantity - (_db.PurchaseReceivingActions.Where(c => c.StoreId == storeId && c.StockDocumentId == stockDocumentId &&
                        c.StockDocumentProvisionalItemId == x.StockDocumentProvisionalItemId && c.Id < x.Id)
                        .OrderByDescending(c => c.Id).Select(c => (decimal?)c.AfterQuantity).FirstOrDefault() ?? 0),
                x.StockDocumentProvisionalItem.ResolvedStockDocumentLine != null
                    ? x.StockDocumentProvisionalItem.ResolvedStockDocumentLine.Factor : x.StockDocumentProvisionalItem.ProposedFactor ?? 1,
                x.StockDocumentProvisionalItem.ResolvedStockDocumentLine != null
                    ? x.StockDocumentProvisionalItem.ResolvedStockDocumentLine.Quantity : x.StockDocumentProvisionalItem.Quantity,
                DateTime.SpecifyKind(x.OccurredAtUtc, DateTimeKind.Utc),
                x.StockDocumentProvisionalItem.IsDeleted || x.StockDocumentProvisionalItem.Status == StockDocumentProvisionalItemStatus.Removed ||
                    (x.StockDocumentProvisionalItem.ResolvedStockDocumentLineId.HasValue && x.StockDocumentProvisionalItem.ResolvedStockDocumentLine == null),
                x.StockDocumentProvisionalItem.PackagingPhoto != null ? x.StockDocumentProvisionalItemId : null))
            .ToListAsync(ct);

    public async Task<int> GetNextLineNoAsync(
        int storeId, int stockDocumentId, CancellationToken ct = default)
    {
        var max = await _db.StockDocumentLines.IgnoreQueryFilters()
            .Where(x => x.StockDocumentId == stockDocumentId &&
                x.StockDocument.StoreId == storeId)
            .Select(x => (int?)x.LineNo).MaxAsync(ct);
        return (max ?? 0) + 1;
    }

    public Task AddItemAsync(StockDocumentProvisionalItem item, CancellationToken ct = default)
        => _db.StockDocumentProvisionalItems.AddAsync(item, ct).AsTask();

    public Task AddActionAsync(PurchaseReceivingAction action, CancellationToken ct = default)
        => _db.PurchaseReceivingActions.AddAsync(action, ct).AsTask();

    public Task AddAuditAsync(PurchaseReceiptAuditEvent auditEvent, CancellationToken ct = default)
        => _db.PurchaseReceiptAuditEvents.AddAsync(auditEvent, ct).AsTask();

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);

    private IQueryable<StockDocument> DocumentQuery(bool tracking)
    {
        var query = _db.StockDocuments
            .Include(x => x.Warehouse).ThenInclude(x => x.LegalEntity)
            .Include(x => x.Supplier)
            .Include(x => x.PurchaseOrder).ThenInclude(x => x!.Supplier)
            .Include(x => x.PurchaseOrder).ThenInclude(x => x!.Lines)
            .Include(x => x.Lines).ThenInclude(x => x.ProductVariant).ThenInclude(x => x.Product)
            .Include(x => x.Lines).ThenInclude(x => x.ProductUnitConversion).ThenInclude(x => x!.Unit)
            .Include(x => x.ProvisionalItems)
            .AsSplitQuery();
        return tracking ? query : query.AsNoTracking();
    }
}
