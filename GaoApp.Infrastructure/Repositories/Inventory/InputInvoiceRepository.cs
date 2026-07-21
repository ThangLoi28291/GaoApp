using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Inventory;

public sealed class InputInvoiceRepository : IInputInvoiceRepository
{
    private readonly AppDbContext _db;

    public InputInvoiceRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<InputInvoiceHead?> GetByXmlHashAsync(
        int storeId,
        string xmlHash,
        CancellationToken ct = default)
    {
        return _db.InputInvoiceHeads
            .Include(x => x.Details)
            .FirstOrDefaultAsync(x =>
                x.StoreId == storeId &&
                x.XmlHash == xmlHash,
                ct);
    }

    public async Task AddInputInvoiceAsync(
        InputInvoiceHead entity,
        CancellationToken ct = default)
    {
        await _db.InputInvoiceHeads.AddAsync(entity, ct);
    }

    public Task<bool> ExistsStockDocumentInvoiceMapAsync(
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

    public async Task AddStockDocumentInvoiceMapAsync(
        StockDocumentInputInvoiceMap entity,
        CancellationToken ct = default)
    {
        await _db.StockDocumentInputInvoiceMaps.AddAsync(entity, ct);
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
        var document = await _db.StockDocuments
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x =>
                x.StoreId == storeId &&
                x.Id == stockDocumentId,
                ct);

        if (document == null)
            throw new InvalidOperationException("Không tìm thấy phiếu nhập kho.");

        var lineIds = document.Lines
            .Where(x => !x.IsDeleted)
            .Select(x => x.Id)
            .ToList();

        var existedLineIds = await _db.StockDocumentLineInputInvoiceMaps
            .Where(x =>
                x.StoreId == storeId &&
                x.StockDocumentId == stockDocumentId)
            .Select(x => x.StockDocumentLineId)
            .ToListAsync(ct);

        var missingLineIds = lineIds.Except(existedLineIds).ToList();

        foreach (var lineId in missingLineIds)
        {
            await _db.StockDocumentLineInputInvoiceMaps.AddAsync(new StockDocumentLineInputInvoiceMap
            {
                StoreId = storeId,
                StockDocumentId = stockDocumentId,
                StockDocumentLineId = lineId,

                // Mặc định không ép thuộc XML.
                // UI bước sau sẽ cho chọn all hoặc bật từng dòng.
                UseInputInvoice = false,
                InputInvoiceDetailId = null,
                MatchStatus = InputInvoiceMatchStatus.None,
                QuantityDifference = 0,
                AmountDifference = 0
            }, ct);
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
        int stockDocumentLineId,
        CancellationToken ct = default)
    {
        return _db.StockDocumentLineInputInvoiceMaps
            .Include(x => x.StockDocumentLine)
            .FirstOrDefaultAsync(x =>
                x.StoreId == storeId &&
                x.StockDocumentLineId == stockDocumentLineId,
                ct);
    }

    public Task<InputInvoiceDetail?> GetInputInvoiceDetailAsync(
        int inputInvoiceDetailId,
        CancellationToken ct = default)
    {
        return _db.InputInvoiceDetails
            .FirstOrDefaultAsync(x => x.Id == inputInvoiceDetailId, ct);
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
}