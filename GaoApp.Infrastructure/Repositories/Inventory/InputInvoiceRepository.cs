
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Helpers;
using GaoApp.Application.DTOs.Inventory.InputInvoices;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;

namespace GaoApp.Infrastructure.Repositories.Inventory;

public sealed class InputInvoiceRepository : IInputInvoiceRepository
{
    private const string XmlHashIndexName =
        "UX_InputInvoiceHead_StoreId_XmlHash_Active";
    private const string BusinessIdentityIndexName =
        "UX_InputInvoiceHead_StoreId_BusinessIdentity_Active";
    private const string ReceiptInvoiceMapIndexName =
        "IX_StockDocumentInputInvoiceMap_StoreId_StockDocumentId_InputInvoiceHeadId";
    private const string ActiveReceiptInvoiceMapIndexName =
        StockDocumentInputInvoiceMap.ActiveReceiptIndexName;
    private const string ReceiptLineMapIndexName =
        "IX_StockDocumentLineInputInvoiceMap_StoreId_StockDocumentLineId";
    private const string IdentityConflictMessage =
    "Dữ liệu định danh hóa đơn đang mâu thuẫn. " +
    "Vui lòng xử lý dữ liệu trước khi liên kết.";
    private const string IncompleteIdentityMessage =
    "Dữ liệu định danh hóa đơn chưa đầy đủ.";

    private readonly AppDbContext _db;
    private IDbContextTransaction? _supplierResolutionTransaction;
    private bool _ownsSupplierResolutionTransaction;
    private bool _lateAssociationMutationBoundaryActive;

    public InputInvoiceRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task BeginSupplierResolutionTransactionAsync(
        CancellationToken ct = default)
    {
        _lateAssociationMutationBoundaryActive = true;
        if (_db.Database.CurrentTransaction is not null)
            return;
        if (!_db.Database.IsRelational())
        {
            _ownsSupplierResolutionTransaction = false;
            return;
        }

        try
        {
            _supplierResolutionTransaction = await _db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                ct);
            _ownsSupplierResolutionTransaction = true;
        }
        catch
        {
            _lateAssociationMutationBoundaryActive = false;
            throw;
        }
    }

    public async Task CommitSupplierResolutionTransactionAsync(
        CancellationToken ct = default)
    {
        try
        {
            if (_ownsSupplierResolutionTransaction && _supplierResolutionTransaction is not null)
                await _supplierResolutionTransaction.CommitAsync(ct);
        }
        finally
        {
            if (_ownsSupplierResolutionTransaction && _supplierResolutionTransaction is not null)
                await _supplierResolutionTransaction.DisposeAsync();
            _supplierResolutionTransaction = null;
            _ownsSupplierResolutionTransaction = false;
            _lateAssociationMutationBoundaryActive = false;
        }
    }

    public async Task RollbackSupplierResolutionTransactionAsync(
        CancellationToken ct = default)
    {
        try
        {
            if (_ownsSupplierResolutionTransaction && _supplierResolutionTransaction is not null)
                await _supplierResolutionTransaction.RollbackAsync(ct);
        }
        finally
        {
            if (_ownsSupplierResolutionTransaction && _supplierResolutionTransaction is not null)
                await _supplierResolutionTransaction.DisposeAsync();
            _supplierResolutionTransaction = null;
            _ownsSupplierResolutionTransaction = false;
            _lateAssociationMutationBoundaryActive = false;
        }
    }

    public async Task<InputInvoiceHead?> LockForSupplierResolutionAsync(
        int storeId,
        int inputInvoiceHeadId,
        CancellationToken ct = default)
    {
        if (_db.Database.IsRelational() && _db.Database.CurrentTransaction is not null)
        {
            return await _db.InputInvoiceHeads
                .FromSqlInterpolated($$"""
                    SELECT * FROM [dbo].[InputInvoiceHead] WITH (UPDLOCK, HOLDLOCK)
                    WHERE [StoreId] = {{storeId}} AND [Id] = {{inputInvoiceHeadId}}
                    """)
                .SingleOrDefaultAsync(ct);
        }

        return await _db.InputInvoiceHeads.SingleOrDefaultAsync(
            x => x.StoreId == storeId && x.Id == inputInvoiceHeadId,
            ct);
    }

    public async Task<IReadOnlyList<StockDocument>> LockLinkedReceiptsForSupplierResolutionAsync(
        int storeId,
        int inputInvoiceHeadId,
        CancellationToken ct = default)
    {
        var receiptIds = await _db.StockDocumentInputInvoiceMaps
            .AsNoTracking()
            .Where(x => x.StoreId == storeId &&
                        x.InputInvoiceHeadId == inputInvoiceHeadId)
            .Select(x => x.StockDocumentId)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(ct);
        var result = new List<StockDocument>(receiptIds.Count);
        foreach (var receiptId in receiptIds)
        {
            StockDocument? receipt;
            if (_db.Database.IsRelational() && _db.Database.CurrentTransaction is not null)
            {
                receipt = await _db.StockDocuments
                    .FromSqlInterpolated($$"""
                        SELECT * FROM [dbo].[StockDocument] WITH (UPDLOCK, HOLDLOCK)
                        WHERE [StoreId] = {{storeId}} AND [Id] = {{receiptId}}
                        """)
                    .SingleOrDefaultAsync(ct);
                if (receipt is not null)
                {
                    await _db.Entry(receipt).Reference(x => x.Supplier).LoadAsync(ct);
                    await _db.Entry(receipt).Reference(x => x.PurchaseOrder).LoadAsync(ct);
                }
            }
            else
            {
                receipt = await _db.StockDocuments
                    .Include(x => x.Supplier)
                    .Include(x => x.PurchaseOrder)
                    .SingleOrDefaultAsync(x =>
                        x.StoreId == storeId && x.Id == receiptId,
                        ct);
            }

            if (receipt is not null)
                result.Add(receipt);
        }

        return result;
    }

    public Task<StockDocument?> GetReceiptForSupplierResolutionAsync(
        int storeId,
        int stockDocumentId,
        CancellationToken ct = default)
        => _db.StockDocuments
            .AsNoTracking()
            .Include(x => x.Supplier)
            .Include(x => x.Warehouse)
                .ThenInclude(x => x.LegalEntity)
            .Include(x => x.ConfirmedLegalEntity)
            .Include(x => x.PurchaseOrder)
            .SingleOrDefaultAsync(x =>
                x.StoreId == storeId && x.Id == stockDocumentId,
                ct);

    public async Task<StockDocument?> LockReceiptForInputInvoiceMutationAsync(
        int storeId,
        int stockDocumentId,
        CancellationToken ct = default)
    {
        StockDocument? receipt;
        if (_db.Database.IsRelational() && _db.Database.CurrentTransaction is not null)
        {
            receipt = await _db.StockDocuments
                .FromSqlInterpolated($$"""
                    SELECT * FROM [dbo].[StockDocument] WITH (UPDLOCK, HOLDLOCK)
                    WHERE [StoreId] = {{storeId}} AND [Id] = {{stockDocumentId}}
                    """)
                .SingleOrDefaultAsync(ct);
            if (receipt is not null)
            {
                await _db.Entry(receipt).Reference(x => x.Supplier).LoadAsync(ct);
                await _db.Entry(receipt).Reference(x => x.Warehouse).LoadAsync(ct);
                await _db.Entry(receipt).Reference(x => x.PurchaseOrder).LoadAsync(ct);
            }
            return receipt;
        }

        return await _db.StockDocuments
            .Include(x => x.Supplier)
            .Include(x => x.Warehouse)
                .ThenInclude(x => x.LegalEntity)
            .Include(x => x.PurchaseOrder)
            .SingleOrDefaultAsync(x =>
                x.StoreId == storeId && x.Id == stockDocumentId,
                ct);
    }

    public async Task<StockDocument?> LockReceiptAggregateForSplitAsync(
        int storeId,
        int stockDocumentId,
        CancellationToken ct = default)
    {
        var receipt = await LockReceiptForInputInvoiceMutationAsync(
            storeId, stockDocumentId, ct);
        if (receipt is null) return null;

        await _db.Entry(receipt).Reference(x => x.Warehouse).LoadAsync(ct);
        await _db.Entry(receipt).Collection(x => x.Lines)
            .Query().OrderBy(x => x.LineNo).LoadAsync(ct);
        await _db.Entry(receipt).Collection(x => x.InputInvoiceMaps)
            .Query().Include(x => x.InputInvoiceHead).LoadAsync(ct);
        await _db.Entry(receipt).Collection(x => x.LineInputInvoiceMaps)
            .Query().Include(x => x.InputInvoiceDetail).LoadAsync(ct);
        return receipt;
    }

    public Task<Supplier?> GetSplitSupplierAsync(
        int storeId,
        int supplierId,
        CancellationToken ct = default)
        => _db.Suppliers.AsNoTracking().SingleOrDefaultAsync(
            x => x.StoreId == storeId && x.Id == supplierId && x.IsActive,
            ct);

    public Task<Warehouse?> GetSplitWarehouseAsync(
        int storeId,
        int warehouseId,
        CancellationToken ct = default)
        => _db.Warehouses.AsNoTracking().SingleOrDefaultAsync(
            x => x.StoreId == storeId && x.Id == warehouseId && x.IsActive,
            ct);

    public async Task<IReadOnlyList<Supplier>> GetSplitSuppliersAsync(
        int storeId,
        CancellationToken ct = default)
        => await _db.Suppliers.AsNoTracking()
            .Where(x => x.StoreId == storeId && x.IsActive)
            .OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct);

    public async Task<IReadOnlyList<Warehouse>> GetSplitWarehousesAsync(
        int storeId,
        CancellationToken ct = default)
        => await _db.Warehouses.AsNoTracking()
            .Where(x => x.StoreId == storeId && x.IsActive)
            .OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct);

    public Task<InputInvoiceHead?> GetLinkedInputInvoiceAsync(
        int storeId,
        int stockDocumentId,
        int inputInvoiceHeadId,
        CancellationToken ct = default)
        => _db.StockDocumentInputInvoiceMaps
            .AsNoTracking()
            .Where(x =>
                x.StoreId == storeId &&
                x.StockDocumentId == stockDocumentId &&
                x.InputInvoiceHeadId == inputInvoiceHeadId &&
                x.InputInvoiceHead.StoreId == storeId &&
                !x.InputInvoiceHead.IsDeleted)
            .Select(x => x.InputInvoiceHead)
            .SingleOrDefaultAsync(ct);

    public async Task<int> DeleteReceiptInputInvoiceLineMapsAsync(
        int storeId,
        int stockDocumentId,
        int inputInvoiceHeadId,
        CancellationToken ct = default)
    {
        var maps = await _db.StockDocumentLineInputInvoiceMaps
            .Where(x =>
                x.StoreId == storeId &&
                x.StockDocumentId == stockDocumentId &&
                x.InputInvoiceDetailId.HasValue &&
                x.InputInvoiceDetail!.InputInvoiceHeadId == inputInvoiceHeadId)
            .ToListAsync(ct);
        _db.StockDocumentLineInputInvoiceMaps.RemoveRange(maps);
        return maps.Count;
    }

    public async Task<bool> DeleteStockDocumentInputInvoiceMapAsync(
        int storeId,
        int stockDocumentId,
        int inputInvoiceHeadId,
        CancellationToken ct = default)
    {
        var map = await _db.StockDocumentInputInvoiceMaps.SingleOrDefaultAsync(x =>
            x.StoreId == storeId &&
            x.StockDocumentId == stockDocumentId &&
            x.InputInvoiceHeadId == inputInvoiceHeadId,
            ct);
        if (map is null)
            return false;
        _db.StockDocumentInputInvoiceMaps.Remove(map);
        return true;
    }

    public async Task<IReadOnlyList<InputInvoiceHead>> GetLinkedInvoicesForSupplierResolutionAsync(
        int storeId,
        int stockDocumentId,
        CancellationToken ct = default)
        => await _db.StockDocumentInputInvoiceMaps
            .AsNoTracking()
            .Where(x => x.StoreId == storeId &&
                        x.StockDocumentId == stockDocumentId)
            .Select(x => x.InputInvoiceHead)
            .Where(x => x.StoreId == storeId)
            .OrderBy(x => x.Id)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<InputInvoiceSupplierResolutionEvent>>
        GetSupplierResolutionEventsAsync(
            int storeId,
            int inputInvoiceHeadId,
            CancellationToken ct = default)
        => await _db.InputInvoiceSupplierResolutionEvents
            .AsNoTracking()
            .Where(x => x.StoreId == storeId &&
                        x.InputInvoiceHeadId == inputInvoiceHeadId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .ToListAsync(ct);

    public Task AddSupplierResolutionEventAsync(
        InputInvoiceSupplierResolutionEvent resolutionEvent,
        CancellationToken ct = default)
        => _db.InputInvoiceSupplierResolutionEvents.AddAsync(
            resolutionEvent,
            ct).AsTask();

    public Task AddPurchaseReceiptAuditEventAsync(
        PurchaseReceiptAuditEvent auditEvent,
        CancellationToken ct = default)
        => _db.PurchaseReceiptAuditEvents.AddAsync(auditEvent, ct).AsTask();

    public Task ClearFailedTransactionStateAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _db.ChangeTracker.Clear();
        return Task.CompletedTask;
    }
    public async Task<InputInvoiceHead?> FindActiveByXmlHashAsync(
    int storeId,
    string xmlHash,
    CancellationToken ct = default)
    {
        if (storeId <= 0)
            throw new BusinessRuleException("StoreId không hợp lệ.");

        if (string.IsNullOrWhiteSpace(xmlHash))
            return null;

        var matches = await _db.InputInvoiceHeads
            .AsNoTracking()
            .Include(x => x.Details)
            .Where(x =>
                x.StoreId == storeId
                && !x.IsDeleted
                && x.XmlHash == xmlHash)
            .Take(2)
            .ToListAsync(ct);

        if (matches.Count > 1)
            throw new BusinessRuleException(
                IdentityConflictMessage);

        return matches.SingleOrDefault();
    }

    public async Task<InputInvoiceResolution> ResolveInputInvoiceAsync(
    InputInvoiceHead entity,
    CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ValidateCompleteCandidate(entity);
        await LockCanonicalInvoiceIdentityAsync(entity, ct);

        var existing =
            await ResolveExistingCompleteCandidateAsync(
                entity,
                ct);

        if (existing is not null)
        {
            await EnrichExistingDetailIdentityAsync(existing, entity, ct);
            return new InputInvoiceResolution(
                existing,
                IsExisting: true);
        }

        await _db.InputInvoiceHeads.AddAsync(entity, ct);

        try
        {
            AssertLateAssociationMutationBoundary();
            await _db.SaveChangesAsync(ct);

            return new InputInvoiceResolution(
                entity,
                IsExisting: false);
        }
        catch (DbUpdateException exception)
            when (IsInvoiceIdentityConflict(exception))
        {
            DetachAddedInvoiceGraph(entity);

            existing =
                await ResolveExistingCompleteCandidateAsync(
                    entity,
                    ct);

            if (existing is null)
                throw;

            await EnrichExistingDetailIdentityAsync(existing, entity, ct);

            return new InputInvoiceResolution(
                existing,
                IsExisting: true);
        }
    }
    public async Task<IReadOnlyList<InputInvoiceHead>> GetByBusinessIdentitiesAsync(
        int storeId,
        IReadOnlyCollection<InputInvoiceBusinessIdentityDto> identities,
        CancellationToken ct = default)
    {
        if (storeId <= 0 || identities.Count == 0)
            return [];

        var taxCodes = identities.Select(x => x.NormalizedSellerTaxCode).Distinct().ToList();
        var minimumDate = identities.Min(x => x.InvoiceIdentityDate.Date);
        var maximumDate = identities.Max(x => x.InvoiceIdentityDate.Date);
        var requested = identities.Select(x =>
                $"{x.NormalizedSellerTaxCode}|{x.NormalizedInvoiceSeries}|" +
                $"{x.NormalizedInvoiceNumber}|{x.InvoiceIdentityDate:yyyy-MM-dd}")
            .ToHashSet(StringComparer.Ordinal);
        var candidates = await _db.InputInvoiceHeads
            .AsNoTracking()
            .Include(x => x.StockDocumentMaps)
            .Where(x => x.StoreId == storeId &&
                        !x.IsDeleted &&
                        x.NormalizedSellerTaxCode != null &&
                        taxCodes.Contains(x.NormalizedSellerTaxCode) &&
                        x.InvoiceIdentityDate >= minimumDate &&
                        x.InvoiceIdentityDate <= maximumDate)
            .ToListAsync(ct);

        return candidates.Where(x => requested.Contains(
                $"{x.NormalizedSellerTaxCode}|{x.NormalizedInvoiceSeries}|" +
                $"{x.NormalizedInvoiceNumber}|{x.InvoiceIdentityDate:yyyy-MM-dd}"))
            .ToList();
    }

    private async Task LockCanonicalInvoiceIdentityAsync(
        InputInvoiceHead entity,
        CancellationToken ct)
    {
        if (!_db.Database.IsRelational() || _db.Database.CurrentTransaction is null)
            return;

        var resource =
            $"GaoApp:InputInvoice:{entity.StoreId}:{entity.NormalizedSellerTaxCode}:" +
            $"{entity.NormalizedInvoiceSeries}:{entity.NormalizedInvoiceNumber}:" +
            $"{entity.InvoiceIdentityDate:yyyyMMdd}";
        await _db.Database.ExecuteSqlInterpolatedAsync($$"""
            EXEC sys.sp_getapplock
                @Resource = {{resource}},
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 15000;
            """, ct);
    }

    public Task<bool> IsStockDocumentInvoiceLinkedAsync(
        int storeId,
        int stockDocumentId,
        int inputInvoiceHeadId,
        CancellationToken ct = default)
        => StockDocumentInvoiceMapExistsAsync(
            storeId, stockDocumentId, inputInvoiceHeadId, ct);

    public Task<bool> EnsureStockDocumentInvoiceMapAsync(
        StockDocumentInputInvoiceMap entity,
        CancellationToken ct = default)
        => EnsureSingleReceiptInvoiceMapAsync(entity, ct);

    public async Task<bool> EnsureSingleReceiptInvoiceMapAsync(
        StockDocumentInputInvoiceMap entity,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var activeMaps = await _db.StockDocumentInputInvoiceMaps
            .Where(x => x.StoreId == entity.StoreId &&
                        x.StockDocumentId == entity.StockDocumentId)
            .OrderBy(x => x.Id)
            .Take(3)
            .ToListAsync(ct);
        if (activeMaps.Count > 1)
            throw new BusinessRuleException(
                "Phiếu nhập đang liên kết nhiều hóa đơn đầu vào. Vui lòng xử lý dữ liệu trước khi tiếp tục.");
        if (activeMaps.Count == 1)
        {
            if (activeMaps[0].InputInvoiceHeadId == entity.InputInvoiceHeadId)
                return false;
            throw new BusinessRuleException(
                "Phiếu nhập đã liên kết một hóa đơn đầu vào. Vui lòng gỡ liên kết trước khi chọn hóa đơn khác.");
        }

        var existingMap = await FindStockDocumentInvoiceMapIncludingDeletedAsync(
                entity.StoreId,
                entity.StockDocumentId,
                entity.InputInvoiceHeadId,
                ct);
        if (existingMap is not null)
        {
            if (!existingMap.IsDeleted)
                return false;

            Reactivate(existingMap);
            AssertLateAssociationMutationBoundary();
            await _db.SaveChangesAsync(ct);
            return true;
        }

        await _db.StockDocumentInputInvoiceMaps.AddAsync(entity, ct);
        try
        {
            AssertLateAssociationMutationBoundary();
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException exception)
            when (IsUniqueConflict(exception, ReceiptInvoiceMapIndexName) ||
                  IsUniqueConflict(exception, ActiveReceiptInvoiceMapIndexName))
        {
            _db.Entry(entity).State = EntityState.Detached;
            if (!await StockDocumentInvoiceMapExistsAsync(
                    entity.StoreId,
                    entity.StockDocumentId,
                    entity.InputInvoiceHeadId,
                    ct))
            {
                var currentInvoiceId = await _db.StockDocumentInputInvoiceMaps
                    .AsNoTracking()
                    .Where(x => x.StoreId == entity.StoreId &&
                                x.StockDocumentId == entity.StockDocumentId)
                    .Select(x => (int?)x.InputInvoiceHeadId)
                    .SingleOrDefaultAsync(ct);
                throw InputInvoiceAssociationException.AssociationChanged(
                    currentInvoiceId);
            }
            return false;
        }
    }

    public async Task<int> ResetReceiptLineMapsAsync(
        int storeId,
        int stockDocumentId,
        CancellationToken ct = default)
    {
        var maps = await _db.StockDocumentLineInputInvoiceMaps
            .Where(x => x.StoreId == storeId && x.StockDocumentId == stockDocumentId)
            .ToListAsync(ct);
        _db.StockDocumentLineInputInvoiceMaps.RemoveRange(maps);
        return maps.Count;
    }

    private Task<bool> StockDocumentInvoiceMapExistsAsync(
        int storeId,
        int stockDocumentId,
        int inputInvoiceHeadId,
        CancellationToken ct = default)
    {
        return _db.StockDocumentInputInvoiceMaps.AnyAsync(x =>
            x.StoreId == storeId &&
            x.StockDocumentId == stockDocumentId &&
            x.InputInvoiceHeadId == inputInvoiceHeadId,
            ct);
    }

    private Task<StockDocumentInputInvoiceMap?>
        FindStockDocumentInvoiceMapIncludingDeletedAsync(
            int storeId,
            int stockDocumentId,
            int inputInvoiceHeadId,
            CancellationToken ct = default)
    {
        return _db.StockDocumentInputInvoiceMaps
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x =>
                x.StoreId == storeId &&
                x.StockDocumentId == stockDocumentId &&
                x.InputInvoiceHeadId == inputInvoiceHeadId,
                ct);
    }

    public Task<StockDocument?> GetStockDocumentWithLinesAsync(
        int storeId,
        int stockDocumentId,
        CancellationToken ct = default)
    {
        return _db.StockDocuments
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x =>
                x.StoreId == storeId &&
                x.Id == stockDocumentId,
                ct);
    }

    public async Task AddMissingLineMapsAsync(
        int storeId,
        int stockDocumentId,
        CancellationToken ct = default)
    {
        const int maximumAttempts = 2;

        for (var attempt = 1;
             attempt <= maximumAttempts;
             attempt++)
        {
            var document = await _db.StockDocuments
                .AsNoTracking()
                .Include(x => x.Lines)
                .FirstOrDefaultAsync(
                    x => x.StoreId == storeId
                         && x.Id == stockDocumentId,
                    ct);

            if (document is null)
            {
                throw new InvalidOperationException(
                    "Không tìm thấy phiếu nhập kho.");
            }

            var activeLineIds = document.Lines
                .Where(x => !x.IsDeleted)
                .Select(x => x.Id)
                .ToList();

            var persistedLineIds = await _db
                .StockDocumentLineInputInvoiceMaps
                .AsNoTracking()
                .Where(x =>
                    x.StoreId == storeId
                    && x.StockDocumentId
                        == stockDocumentId)
                .Select(x => x.StockDocumentLineId)
                .ToListAsync(ct);

            var missingLineIds = activeLineIds
                .Except(persistedLineIds)
                .ToList();

            if (missingLineIds.Count == 0)
                return;

            var deletedMapsByLineId = await _db
                .StockDocumentLineInputInvoiceMaps
                .IgnoreQueryFilters()
                .Where(x =>
                    x.StoreId == storeId
                    && x.StockDocumentId == stockDocumentId
                    && x.IsDeleted
                    && missingLineIds.Contains(
                        x.StockDocumentLineId))
                .ToDictionaryAsync(
                    x => x.StockDocumentLineId,
                    ct);

            foreach (var lineId in missingLineIds)
            {
                if (deletedMapsByLineId.TryGetValue(
                        lineId,
                        out var deletedMap))
                {
                    ReactivateAsUnassigned(deletedMap);
                    continue;
                }

                await _db
                    .StockDocumentLineInputInvoiceMaps
                    .AddAsync(
                        new StockDocumentLineInputInvoiceMap
                        {
                            StoreId = storeId,
                            StockDocumentId =
                                stockDocumentId,
                            StockDocumentLineId = lineId,
                            UseInputInvoice = false,
                            InputInvoiceDetailId = null,
                            MatchStatus =
                                InputInvoiceMatchStatus.None,
                            QuantityDifference = 0,
                            AmountDifference = 0
                        },
                        ct);
            }

            try
            {
                AssertLateAssociationMutationBoundary();
                await _db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException exception)
                when (IsUniqueConflict(
                    exception,
                    ReceiptLineMapIndexName))
            {
                DetachAddedLineMaps(
                    storeId,
                    stockDocumentId);

                var durableStateIsComplete =
                    await HasCompleteDurableLineMapStateAsync(
                        storeId,
                        stockDocumentId,
                        ct);

                if (durableStateIsComplete)
                    return;

                if (attempt == maximumAttempts)
                    throw;
            }
        }
    }

    private static void Reactivate(
        StockDocumentInputInvoiceMap map)
    {
        map.IsDeleted = false;
        map.DeletedAtUtc = null;
        map.DeletedBy = null;
    }

    private static void ReactivateAsUnassigned(
        StockDocumentLineInputInvoiceMap map)
    {
        map.IsDeleted = false;
        map.DeletedAtUtc = null;
        map.DeletedBy = null;
        map.UseInputInvoice = false;
        map.InputInvoiceDetailId = null;
        map.MatchStatus = InputInvoiceMatchStatus.None;
        map.QuantityDifference = 0;
        map.AmountDifference = 0;
        map.Note = null;
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        if (_lateAssociationMutationBoundaryActive)
            AssertLateAssociationMutationBoundary();
        return _db.SaveChangesAsync(ct);
    }

    public void AssertLateAssociationMutationBoundary()
    {
        _db.ChangeTracker.DetectChanges();
        var protectedChanges = _db.ChangeTracker.Entries()
            .Where(entry => entry.State is EntityState.Added or
                EntityState.Modified or EntityState.Deleted)
            .Where(entry => entry.Entity is StockDocument or
                StockDocumentLine or
                InventoryTransaction or
                InventoryBalance or
                InventoryValuationEntry or
                InventoryCostLayer or
                InventoryCostLayerAllocation or
                PurchasePayable or
                PurchaseOrder or
                PurchaseOrderLine)
            .Select(entry => entry.Metadata.ClrType.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (protectedChanges.Length > 0)
            throw new BusinessRuleException(
                "Late XML association cannot mutate posted receipt effects: " +
                string.Join(", ", protectedChanges));
    }
    public async Task<List<InputInvoiceHead>> GetByStockDocumentAsync(
    int storeId,
    int stockDocumentId,
    CancellationToken ct = default)
    {
        return await _db.StockDocumentInputInvoiceMaps
            .AsNoTracking()
            .Where(x =>
                x.StoreId == storeId &&
                x.StockDocumentId == stockDocumentId)
            .Include(x => x.InputInvoiceHead)
                .ThenInclude(x => x.Details)
            .Include(x => x.InputInvoiceHead)
                .ThenInclude(x => x.ResolvedBuyerLegalEntity)
            .Select(x => x.InputInvoiceHead)
            .OrderByDescending(x => x.InvoiceDate)
            .ThenByDescending(x => x.Id)
            .ToListAsync(ct);
    }
    public async Task<List<StockDocumentLineInputInvoiceMap>> GetLineMapsByStockDocumentAsync(
    int storeId,
    int stockDocumentId,
    CancellationToken ct = default)
    {
        return await _db.StockDocumentLineInputInvoiceMaps
            .Include(x => x.InputInvoiceDetail)
            .Where(x =>
                x.StoreId == storeId &&
                x.StockDocumentId == stockDocumentId)
            .OrderBy(x => x.StockDocumentLineId)
            .ToListAsync(ct);
    }

    public Task<StockDocumentLineInputInvoiceMap?> GetLineMapAsync(
        int storeId,
        int stockDocumentId,
        int stockDocumentLineId,
        CancellationToken ct = default)
    {
        return _db.StockDocumentLineInputInvoiceMaps
            .Include(x => x.StockDocumentLine)
            .FirstOrDefaultAsync(x =>
                x.StoreId == storeId &&
                x.StockDocumentId == stockDocumentId &&
                x.StockDocumentLineId == stockDocumentLineId,
                ct);
    }

    public async Task<InputInvoiceDetail?> GetInputInvoiceDetailAsync(
        int storeId,
        int stockDocumentId,
        int stockDocumentLineId,
        int inputInvoiceDetailId,
        CancellationToken ct = default)
    {
        var detailResult = await (
            from detail in _db.InputInvoiceDetails
            join head in _db.InputInvoiceHeads
                on detail.InputInvoiceHeadId equals head.Id
            join invoiceMap in _db.StockDocumentInputInvoiceMaps
                on head.Id equals invoiceMap.InputInvoiceHeadId
            join receipt in _db.StockDocuments
                on invoiceMap.StockDocumentId equals receipt.Id
            join receiptLine in _db.StockDocumentLines
                on receipt.Id equals receiptLine.StockDocumentId
            join receiptWarehouse in _db.Warehouses
                on receipt.WarehouseId equals receiptWarehouse.Id
            where detail.Id == inputInvoiceDetailId
                  && head.StoreId == storeId
                  && invoiceMap.StoreId == storeId
                  && invoiceMap.StockDocumentId == stockDocumentId
                  && receipt.Id == stockDocumentId
                  && receipt.StoreId == storeId
                  && receipt.Type == StockDocumentType.Receipt
                  && receiptLine.Id == stockDocumentLineId
                  && receiptWarehouse.StoreId == storeId
                  && receiptWarehouse.LegalEntityId > 0
                  && !_db.StockDocumentInputInvoiceMaps.Any(otherMap =>
                      otherMap.StoreId == storeId
                      && otherMap.InputInvoiceHeadId == head.Id
                      && _db.StockDocuments.Any(otherReceipt =>
                          otherReceipt.Id == otherMap.StockDocumentId
                          && otherReceipt.StoreId == storeId
                          && _db.Warehouses.Any(otherWarehouse =>
                              otherWarehouse.Id == otherReceipt.WarehouseId
                              && otherWarehouse.StoreId == storeId
                              && otherWarehouse.LegalEntityId != receiptWarehouse.LegalEntityId)))
            select detail)
            .FirstOrDefaultAsync(ct);
        if (detailResult is not null)
            await _db.Entry(detailResult)
                .Reference(x => x.InputInvoiceHead)
                .LoadAsync(ct);
        return detailResult;
    }

    public Task<StockDocumentLine?> GetStockDocumentLineAsync(
        int storeId,
        int stockDocumentLineId,
        CancellationToken ct = default)
    {
        return _db.StockDocumentLines
            .Include(x => x.StockDocument)
            .FirstOrDefaultAsync(x =>
                x.Id == stockDocumentLineId &&
                x.StockDocument.StoreId == storeId,
                ct);
    }

    public async Task AcquireInputInvoiceItemCatalogKeyLockAsync(
        int storeId,
        int supplierId,
        string normalizedItemIdentity,
        string normalizedUnitName,
        CancellationToken ct = default)
    {
        if (!_db.Database.IsRelational() ||
            _db.Database.CurrentTransaction is null)
            return;

        // Serialize learning for one authoritative supplier item identity. Different
        // XML units still persist under distinct unique keys, while the wider lock
        // prevents SQL index/audit deadlocks when those unit rows are learned together.
        var raw = $"{storeId}|{supplierId}|{normalizedItemIdentity}";
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(raw)));
        var resource = $"GaoApp:InputInvoiceItem:{hash}";
        await _db.Database.ExecuteSqlInterpolatedAsync($$"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = {{resource}},
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 15000;
            IF @result < 0
                THROW 51011, 'Unable to acquire input-invoice item mapping lock.', 1;
            """, ct);
    }

    public async Task<InputInvoiceItemCatalogMap?> GetInputInvoiceItemCatalogMapAsync(
        int storeId,
        int supplierId,
        string? normalizedSupplierItemCode,
        string normalizedSupplierItemName,
        string normalizedSupplierUnitName,
        bool tracking,
        CancellationToken ct = default)
    {
        IQueryable<InputInvoiceItemCatalogMap> query = _db.InputInvoiceItemCatalogMaps
            .Include(x => x.ProductVariant)
                .ThenInclude(x => x.Product)
                    .ThenInclude(x => x.BaseUnit)
            .Include(x => x.ProductUnitConversion)
                .ThenInclude(x => x.Unit)
            .Include(x => x.ConfirmedUnit)
            .Include(x => x.ConfirmedBaseUnit)
            .Where(x => x.StoreId == storeId &&
                        x.SupplierId == supplierId &&
                        x.IsActive);
        if (!tracking)
            query = query.AsNoTracking();

        return normalizedSupplierItemCode is not null
            ? await query.SingleOrDefaultAsync(x =>
                x.NormalizedSupplierItemCode == normalizedSupplierItemCode &&
                x.NormalizedSupplierUnitName == normalizedSupplierUnitName, ct)
            : await query.SingleOrDefaultAsync(x =>
                x.NormalizedSupplierItemCode == null &&
                x.NormalizedSupplierItemName == normalizedSupplierItemName &&
                x.NormalizedSupplierUnitName == normalizedSupplierUnitName, ct);
    }

    public async Task<IReadOnlyList<InputInvoiceItemCatalogMap>>
        GetActiveInputInvoiceItemCatalogMapsByCodeAsync(
            int storeId,
            int supplierId,
            string normalizedSupplierItemCode,
            CancellationToken ct = default)
        => await _db.InputInvoiceItemCatalogMaps
            .AsNoTracking()
            .Include(x => x.ProductVariant)
                .ThenInclude(x => x.Product)
                    .ThenInclude(x => x.BaseUnit)
            .Include(x => x.ProductUnitConversion)
                .ThenInclude(x => x.Unit)
            .Include(x => x.ConfirmedUnit)
            .Include(x => x.ConfirmedBaseUnit)
            .Where(x => x.StoreId == storeId &&
                        x.SupplierId == supplierId &&
                        x.NormalizedSupplierItemCode == normalizedSupplierItemCode &&
                        x.IsActive &&
                        !x.IsDeleted)
            .OrderBy(x => x.NormalizedSupplierUnitName)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);

    public async Task<InputInvoiceItemCatalogTarget?> GetInputInvoiceItemCatalogTargetAsync(
        int storeId,
        int productVariantId,
        int productUnitConversionId,
        CancellationToken ct = default)
    {
        var conversion = await _db.ProductUnitConversions
            .AsNoTracking()
            .Include(x => x.Unit)
            .Include(x => x.ProductVariant)
                .ThenInclude(x => x.Product)
                    .ThenInclude(x => x.BaseUnit)
            .SingleOrDefaultAsync(x =>
                x.StoreId == storeId &&
                x.Id == productUnitConversionId &&
                x.ProductVariantId == productVariantId, ct);
        if (conversion?.ProductVariant?.Product?.BaseUnit is null ||
            conversion.Unit is null)
            return null;

        return new InputInvoiceItemCatalogTarget(
            conversion.ProductVariant,
            conversion,
            conversion.ProductVariant.Product,
            conversion.Unit,
            conversion.ProductVariant.Product.BaseUnit);
    }

    public async Task<IReadOnlyList<InputInvoiceItemCatalogTarget>>
        GetInputInvoiceItemCatalogTargetsByUnitAsync(
            int storeId,
            int productVariantId,
            string normalizedUnitName,
            CancellationToken ct = default)
    {
        var candidates = await _db.ProductUnitConversions
            .AsNoTracking()
            .Include(x => x.Unit)
            .Include(x => x.ProductVariant)
                .ThenInclude(x => x.Product)
                    .ThenInclude(x => x.BaseUnit)
            .Where(x => x.StoreId == storeId &&
                        x.ProductVariantId == productVariantId &&
                        x.IsActive &&
                        !x.IsDeleted &&
                        x.Factor > 0m &&
                        x.Unit.IsActive &&
                        !x.Unit.IsDeleted &&
                        x.ProductVariant.IsActive &&
                        !x.ProductVariant.IsDeleted &&
                        x.ProductVariant.Product.IsActive &&
                        !x.ProductVariant.Product.IsDeleted &&
                        x.ProductVariant.Product.BaseUnit.IsActive &&
                        !x.ProductVariant.Product.BaseUnit.IsDeleted)
            .OrderBy(x => x.Id)
            .ToListAsync(ct);

        return candidates
            .Where(x => string.Equals(
                            InputInvoiceItemIdentityNormalizer.NormalizeText(x.Unit.Name),
                            normalizedUnitName, StringComparison.Ordinal) ||
                        string.Equals(
                            InputInvoiceItemIdentityNormalizer.NormalizeText(x.Unit.Code),
                            normalizedUnitName, StringComparison.Ordinal))
            .Select(x => new InputInvoiceItemCatalogTarget(
                x.ProductVariant,
                x,
                x.ProductVariant.Product,
                x.Unit,
                x.ProductVariant.Product.BaseUnit))
            .DistinctBy(x => x.Conversion.Id)
            .ToList();
    }

    public Task AddInputInvoiceItemCatalogMapAsync(
        InputInvoiceItemCatalogMap map,
        CancellationToken ct = default)
        => _db.InputInvoiceItemCatalogMaps.AddAsync(map, ct).AsTask();

    public async Task<StockDocument?> LockReceiptForReconciliationAsync(
        int storeId,
        int stockDocumentId,
        CancellationToken ct = default)
    {
        var receipt = await LockReceiptForInputInvoiceMutationAsync(
            storeId, stockDocumentId, ct);
        if (receipt is null) return null;

        await _db.Entry(receipt).Collection(x => x.Lines)
            .Query().OrderBy(x => x.LineNo).LoadAsync(ct);
        await _db.Entry(receipt).Collection(x => x.InputInvoiceMaps)
            .Query()
            .Include(x => x.InputInvoiceHead)
                .ThenInclude(x => x.Details)
            .LoadAsync(ct);
        await _db.Entry(receipt).Collection(x => x.LineInputInvoiceMaps)
            .Query()
            .Include(x => x.StockDocumentLine)
            .Include(x => x.InputInvoiceDetail)
            .LoadAsync(ct);
        return receipt;
    }

    public async Task<List<StockDocumentInputInvoiceReconciliation>> GetReconciliationsAsync(
        int storeId,
        int stockDocumentId,
        bool tracking,
        CancellationToken ct = default)
    {
        IQueryable<StockDocumentInputInvoiceReconciliation> query =
            _db.StockDocumentInputInvoiceReconciliations
                .Include(x => x.Details)
                    .ThenInclude(x => x.InputInvoiceDetail)
                .Where(x => x.StoreId == storeId &&
                            x.StockDocumentId == stockDocumentId);
        if (!tracking) query = query.AsNoTracking();
        return await query.OrderBy(x => x.InputInvoiceHeadId).ToListAsync(ct);
    }

    public Task AddReconciliationAsync(
        StockDocumentInputInvoiceReconciliation reconciliation,
        CancellationToken ct = default)
        => _db.StockDocumentInputInvoiceReconciliations.AddAsync(
            reconciliation, ct).AsTask();

    public Task AddDetailReconciliationsAsync(
        IEnumerable<StockDocumentInputInvoiceDetailReconciliation> details,
        CancellationToken ct = default)
        => _db.StockDocumentInputInvoiceDetailReconciliations
            .AddRangeAsync(details, ct);

    public async Task<int> DeleteReconciliationsAsync(
        int storeId,
        int stockDocumentId,
        int? inputInvoiceHeadId = null,
        CancellationToken ct = default)
    {
        var rows = await _db.StockDocumentInputInvoiceReconciliations
            .Include(x => x.Details)
            .Where(x => x.StoreId == storeId &&
                        x.StockDocumentId == stockDocumentId &&
                        (!inputInvoiceHeadId.HasValue ||
                         x.InputInvoiceHeadId == inputInvoiceHeadId.Value))
            .ToListAsync(ct);
        _db.StockDocumentInputInvoiceDetailReconciliations.RemoveRange(
            rows.SelectMany(x => x.Details));
        _db.StockDocumentInputInvoiceReconciliations.RemoveRange(rows);
        return rows.Count;
    }

    private async Task EnrichExistingDetailIdentityAsync(
        InputInvoiceHead existing,
        InputInvoiceHead candidate,
        CancellationToken ct)
    {
        var candidateByLine = candidate.Details
            .GroupBy(x => x.LineNo)
            .Where(x => x.Count() == 1)
            .ToDictionary(x => x.Key, x => x.Single());
        var existingLineCounts = existing.Details
            .GroupBy(x => x.LineNo)
            .ToDictionary(x => x.Key, x => x.Count());
        var changed = false;
        foreach (var detail in existing.Details)
        {
            detail.NormalizedItemName ??=
                InputInvoiceItemIdentityNormalizer.NormalizeText(detail.ItemName);
            detail.NormalizedUnitName ??=
                InputInvoiceItemIdentityNormalizer.NormalizeText(detail.UnitName);

            if (existingLineCounts[detail.LineNo] != 1 ||
                !candidateByLine.TryGetValue(detail.LineNo, out var parsed) ||
                !string.Equals(
                    detail.NormalizedItemName,
                    parsed.NormalizedItemName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    detail.NormalizedUnitName,
                    parsed.NormalizedUnitName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (detail.SupplierItemCode is null &&
                parsed.SupplierItemCode is not null)
            {
                detail.SupplierItemCode = parsed.SupplierItemCode;
                detail.NormalizedSupplierItemCode =
                    parsed.NormalizedSupplierItemCode;
                changed = true;
            }
        }

        changed = changed || _db.ChangeTracker
            .Entries<InputInvoiceDetail>()
            .Any(x => x.State == EntityState.Modified);
        if (changed)
        {
            AssertLateAssociationMutationBoundary();
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task<InputInvoiceHead?>
    ResolveExistingCompleteCandidateAsync(
        InputInvoiceHead entity,
        CancellationToken ct)
    {
        var hasHash = !string.IsNullOrWhiteSpace(
            entity.XmlHash);

        var matches = await _db.InputInvoiceHeads
            .Include(x => x.Details)
            .Where(x =>
                x.StoreId == entity.StoreId
                && !x.IsDeleted
                && ((hasHash
                        && x.XmlHash == entity.XmlHash)
                    || (x.NormalizedSellerTaxCode
                            == entity.NormalizedSellerTaxCode
                        && x.NormalizedInvoiceSeries
                            == entity.NormalizedInvoiceSeries
                        && x.NormalizedInvoiceNumber
                            == entity.NormalizedInvoiceNumber
                        && x.InvoiceIdentityDate
                            == entity.InvoiceIdentityDate)))
            .Take(4)
            .ToListAsync(ct);

        var businessMatches = matches
            .Where(x =>
                HasSameBusinessIdentity(x, entity))
            .ToList();

        var hashMatches = matches
            .Where(x =>
                hasHash
                && string.Equals(
                    x.XmlHash,
                    entity.XmlHash,
                    StringComparison.Ordinal))
            .ToList();

        if (businessMatches.Count > 1
            || hashMatches.Count > 1)
        {
            throw new BusinessRuleException(
                IdentityConflictMessage);
        }

        var businessMatch =
            businessMatches.SingleOrDefault();

        var hashMatch =
            hashMatches.SingleOrDefault();

        if (businessMatch is not null
            && hashMatch is not null)
        {
            if (businessMatch.Id != hashMatch.Id)
            {
                throw new BusinessRuleException(
                    IdentityConflictMessage);
            }

            return businessMatch;
        }

        if (businessMatch is not null)
            return businessMatch;

        if (hashMatch is null)
            return null;

        // D4 Option A:
        // exact-hash legacy row chưa đủ business identity
        // vẫn được reuse nhưng tuyệt đối không enrich/overwrite.
        if (!HasCompleteBusinessIdentity(hashMatch))
            return hashMatch;

        if (!HasSameBusinessIdentity(
                hashMatch,
                entity))
        {
            throw new BusinessRuleException(
                IdentityConflictMessage);
        }

        return hashMatch;
    }

    private static void ValidateCompleteCandidate(
        InputInvoiceHead entity)
    {
        if (entity.StoreId <= 0)
        {
            throw new BusinessRuleException(
                "StoreId không hợp lệ.");
        }

        if (!HasCompleteBusinessIdentity(entity))
        {
            throw new BusinessRuleException(
                IncompleteIdentityMessage);
        }
    }

    private static bool HasCompleteBusinessIdentity(
        InputInvoiceHead entity)
        => !string.IsNullOrWhiteSpace(
                entity.NormalizedSellerTaxCode)
            && !string.IsNullOrWhiteSpace(
                entity.NormalizedInvoiceSeries)
            && !string.IsNullOrWhiteSpace(
                entity.NormalizedInvoiceNumber)
            && entity.InvoiceIdentityDate.HasValue;

    private static bool HasSameBusinessIdentity(
        InputInvoiceHead left,
        InputInvoiceHead right)
        => string.Equals(
                left.NormalizedSellerTaxCode,
                right.NormalizedSellerTaxCode,
                StringComparison.Ordinal)
            && string.Equals(
                left.NormalizedInvoiceSeries,
                right.NormalizedInvoiceSeries,
                StringComparison.Ordinal)
            && string.Equals(
                left.NormalizedInvoiceNumber,
                right.NormalizedInvoiceNumber,
                StringComparison.Ordinal)
            && left.InvoiceIdentityDate
                == right.InvoiceIdentityDate;
    private void DetachAddedInvoiceGraph(InputInvoiceHead entity)
    {
        foreach (var detail in entity.Details.ToList())
        {
            var detailEntry = _db.Entry(detail);
            if (detailEntry.State == EntityState.Added)
                detailEntry.State = EntityState.Detached;
        }

        var headEntry = _db.Entry(entity);
        if (headEntry.State == EntityState.Added)
            headEntry.State = EntityState.Detached;
    }
    private async Task<bool>
    HasCompleteDurableLineMapStateAsync(
        int storeId,
        int stockDocumentId,
        CancellationToken ct)
    {
        var document = await _db.StockDocuments
            .AsNoTracking()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(
                x => x.StoreId == storeId
                     && x.Id == stockDocumentId,
                ct);

        if (document is null)
            return false;

        var activeLineIds = document.Lines
            .Where(x => !x.IsDeleted)
            .Select(x => x.Id)
            .ToList();

        if (activeLineIds.Count == 0)
            return true;

        var persistedLineIds = await _db
            .StockDocumentLineInputInvoiceMaps
            .AsNoTracking()
            .Where(x =>
                x.StoreId == storeId
                && x.StockDocumentId
                    == stockDocumentId
                && activeLineIds.Contains(
                    x.StockDocumentLineId))
            .Select(x => x.StockDocumentLineId)
            .ToListAsync(ct);

        var persistedLineIdSet =
            persistedLineIds.ToHashSet();

        return persistedLineIds.Count
                   == activeLineIds.Count
               && persistedLineIdSet.Count
                   == activeLineIds.Count
               && activeLineIds.All(
                   persistedLineIdSet.Contains);
    }
    private void DetachAddedLineMaps(int storeId, int stockDocumentId)
    {
        var addedMaps = _db.ChangeTracker
            .Entries<StockDocumentLineInputInvoiceMap>()
            .Where(entry =>
                entry.State == EntityState.Added
                && entry.Entity.StoreId == storeId
                && entry.Entity.StockDocumentId == stockDocumentId)
            .ToList();
        foreach (var entry in addedMaps)
            entry.State = EntityState.Detached;
    }

    private static bool IsInvoiceIdentityConflict(
        DbUpdateException exception)
        => IsUniqueConflict(exception, XmlHashIndexName)
            || IsUniqueConflict(exception, BusinessIdentityIndexName);

    private static bool IsUniqueConflict(
        Exception exception,
        string indexName)
    {
        var sqlException = FindSqlException(exception);
        return sqlException?.Number is 2601 or 2627
            && sqlException.Message.Contains(
                indexName,
                StringComparison.Ordinal);
    }

    private static SqlException? FindSqlException(Exception exception)
    {
        for (Exception? current = exception;
             current is not null;
             current = current.InnerException)
        {
            if (current is SqlException sqlException)
                return sqlException;
        }

        return null;
    }
}
