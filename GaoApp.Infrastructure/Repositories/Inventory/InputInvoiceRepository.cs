
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Inventory;

public sealed class InputInvoiceRepository : IInputInvoiceRepository
{
    private const string XmlHashIndexName =
        "UX_InputInvoiceHead_StoreId_XmlHash_Active";
    private const string BusinessIdentityIndexName =
        "UX_InputInvoiceHead_StoreId_BusinessIdentity_Active";
    private const string ReceiptInvoiceMapIndexName =
        "IX_StockDocumentInputInvoiceMap_StoreId_StockDocumentId_InputInvoiceHeadId";
    private const string ReceiptLineMapIndexName =
        "IX_StockDocumentLineInputInvoiceMap_StoreId_StockDocumentLineId";
    private const string IdentityConflictMessage =
    "Dữ liệu định danh hóa đơn đang mâu thuẫn. " +
    "Vui lòng xử lý dữ liệu trước khi liên kết.";
    private const string IncompleteIdentityMessage =
    "Dữ liệu định danh hóa đơn chưa đầy đủ.";

    private readonly AppDbContext _db;

    public InputInvoiceRepository(AppDbContext db)
    {
        _db = db;
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

        var existing =
            await ResolveExistingCompleteCandidateAsync(
                entity,
                ct);

        if (existing is not null)
        {
            return new InputInvoiceResolution(
                existing,
                IsExisting: true);
        }

        await _db.InputInvoiceHeads.AddAsync(entity, ct);

        try
        {
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

            return new InputInvoiceResolution(
                existing,
                IsExisting: true);
        }
    }
    public async Task EnsureStockDocumentInvoiceMapAsync(
        StockDocumentInputInvoiceMap entity,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (await StockDocumentInvoiceMapExistsAsync(
                entity.StoreId,
                entity.StockDocumentId,
                entity.InputInvoiceHeadId,
                ct))
        {
            return;
        }

        await _db.StockDocumentInputInvoiceMaps.AddAsync(entity, ct);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception)
            when (IsUniqueConflict(exception, ReceiptInvoiceMapIndexName))
        {
            _db.Entry(entity).State = EntityState.Detached;
            if (!await StockDocumentInvoiceMapExistsAsync(
                    entity.StoreId,
                    entity.StockDocumentId,
                    entity.InputInvoiceHeadId,
                    ct))
            {
                throw;
            }
        }
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

            foreach (var lineId in missingLineIds)
            {
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

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _db.SaveChangesAsync(ct);
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

    public Task<InputInvoiceDetail?> GetInputInvoiceDetailAsync(
        int storeId,
        int stockDocumentId,
        int stockDocumentLineId,
        int inputInvoiceDetailId,
        CancellationToken ct = default)
    {
        return (
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

    private async Task<InputInvoiceHead?>
    ResolveExistingCompleteCandidateAsync(
        InputInvoiceHead entity,
        CancellationToken ct)
    {
        var hasHash = !string.IsNullOrWhiteSpace(
            entity.XmlHash);

        var matches = await _db.InputInvoiceHeads
            .AsNoTracking()
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
