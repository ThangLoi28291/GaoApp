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
                .ThenInclude(x => x.LegalEntity)
            .Include(x => x.Supplier)
            .Include(x => x.PurchaseOrder)
            .Include(x => x.PurchasePayables)

            // STOCKDOC.UI.1B:
            // Include ảnh chính của variant để hiển thị ở bảng dòng nhập.
            .Include(x => x.Lines)
                .ThenInclude(x => x.PurchaseOrderLine)
            .Include(x => x.Lines)
                .ThenInclude(x => x.ProductVariant)
                    .ThenInclude(x => x.Product)
            .Include(x => x.Lines)
    .ThenInclude(x => x.ProductVariant)
        .ThenInclude(x => x.PrimaryProductImage!)
            .ThenInclude(x => x.MediaAsset)

            .Include(x => x.LineInputInvoiceMaps)
                .ThenInclude(x => x.InputInvoiceDetail)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<StockDocument?> GetForConfirmAsync(int id, CancellationToken ct = default)
    {
        return await _context.StockDocuments
            .Include(x => x.Warehouse)
                .ThenInclude(x => x.LegalEntity)
            .Include(x => x.Supplier)
            .Include(x => x.Lines)
    .ThenInclude(x => x.PurchaseOrderLine!)
        .ThenInclude(x => x.PurchaseOrder)
            .Include(x => x.PurchaseOrder)
                .ThenInclude(x => x!.Lines)
            .Include(x => x.PurchaseOrder)
                .ThenInclude(x => x!.Supplier)
            .Include(x => x.PurchaseOrder)
                .ThenInclude(x => x!.Actions)
            .Include(x => x.PurchasePayables)
            .Include(x => x.LineInputInvoiceMaps)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<StockDocumentLine?> GetLineByIdAsync(int lineId, CancellationToken ct = default)
    {
        var currentStoreId = _context.CurrentStoreId;

        return await _context.StockDocumentLines
            .Include(x => x.StockDocument)
            .FirstOrDefaultAsync(x =>
                x.Id == lineId &&
                (!currentStoreId.HasValue ||
                 x.StockDocument.StoreId == currentStoreId.Value),
                ct);
    }

    public async Task<int> GetNextLineNoAsync(int stockDocumentId, CancellationToken ct = default)
    {
        var currentStoreId = _context.CurrentStoreId;

        var maxLineNo = await _context.StockDocumentLines
            .IgnoreQueryFilters()
            .Where(x =>
                x.StockDocumentId == stockDocumentId &&
                (!currentStoreId.HasValue ||
                 x.StockDocument.StoreId == currentStoreId.Value))
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

    public Task<Supplier?> GetSupplierAsync(int supplierId, CancellationToken ct = default)
        => _context.Suppliers.FirstOrDefaultAsync(x => x.Id == supplierId, ct);

    public async Task<ProductVariant?> GetVariantForStockDocumentAsync(
        int variantId,
        CancellationToken ct = default)
    {
        return await _context.ProductVariants
            .Include(x => x.Product)
                .ThenInclude(x => x.Tax)
        .Include(x => x.PrimaryProductImage!)
    .ThenInclude(x => x.MediaAsset)
            .Include(x => x.UnitConversions)
                .ThenInclude(x => x.Barcodes)
            .FirstOrDefaultAsync(x => x.Id == variantId && !x.IsDeleted, ct);
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

    public Task<Tax?> GetTaxAsync(int taxId, CancellationToken ct = default)
        => _context.Taxes.FirstOrDefaultAsync(x => x.Id == taxId && x.IsActive, ct);

    public Task<List<Tax>> GetTaxesAsync(CancellationToken ct = default)
        => _context.Taxes
            .AsNoTracking()
            .Where(x => x.IsActive && !x.IsDeleted)
            .OrderBy(x => x.Rate)
            .ThenBy(x => x.Name)
            .ToListAsync(ct);

    public async Task<Dictionary<int, decimal>> GetLastPurchaseBaseUnitPricesBeforeVatAsync(
        IEnumerable<int> productVariantIds,
        CancellationToken ct = default)
    {
        var ids = productVariantIds.Where(x => x > 0).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, decimal>();

        var rows = await _context.StockDocumentLines
            .AsNoTracking()
            .Where(x => !x.IsDeleted &&
                        ids.Contains(x.ProductVariantId) &&
                        x.Factor > 0m &&
                        x.UnitPriceBeforeVat > 0m &&
                        x.StockDocument.Type == StockDocumentType.Receipt &&
                        x.StockDocument.Status == StockDocumentStatus.Confirmed)
            .Select(x => new
            {
                x.ProductVariantId,
                BaseUnitPriceBeforeVat = x.UnitPriceBeforeVat / x.Factor,
                ApprovedAtUtc = x.StockDocument.ApprovedAtUtc,
                x.StockDocument.DocumentDate,
                StockDocumentId = x.StockDocumentId,
                LineId = x.Id
            })
            .ToListAsync(ct);

        return rows
            .GroupBy(x => x.ProductVariantId)
            .ToDictionary(
                x => x.Key,
                x => x.OrderByDescending(v => v.ApprovedAtUtc ?? v.DocumentDate)
                    .ThenByDescending(v => v.StockDocumentId)
                    .ThenByDescending(v => v.LineId)
                    .First().BaseUnitPriceBeforeVat);
    }

    public Task<PurchaseOrder?> GetPurchaseOrderForReceiptAsync(int purchaseOrderId, CancellationToken ct = default)
        => _context.PurchaseOrders
            .Include(x => x.Supplier)
            .Include(x => x.ExpectedWarehouse)
            .Include(x => x.LegalEntity)
            .Include(x => x.Lines).ThenInclude(x => x.ProductVariant).ThenInclude(x => x!.Product)
            .Include(x => x.Lines).ThenInclude(x => x.ProductUnitConversion).ThenInclude(x => x!.Unit)
            .Include(x => x.Actions)
            .FirstOrDefaultAsync(x => x.Id == purchaseOrderId, ct);

    public Task AddPurchasePayableAsync(PurchasePayable payable, CancellationToken ct = default)
        => _context.PurchasePayables.AddAsync(payable, ct).AsTask();

    public Task<bool> PurchasePayableExistsAsync(string sourceKey, CancellationToken ct = default)
        => _context.PurchasePayables.AnyAsync(x => x.SourceKey == sourceKey, ct);

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
            .AsNoTracking()
            .Include(x => x.Warehouse)
                .ThenInclude(x => x.LegalEntity)
            .Include(x => x.Supplier)
            .Include(x => x.Lines)
            .Where(x => x.Type == StockDocumentType.Receipt && !x.IsDeleted)
            .OrderByDescending(x => x.DocumentDate)
            .ThenByDescending(x => x.Id)
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
    public async Task MarkVariantsHasInputInvoiceAsync(
    IEnumerable<int> productVariantIds,
    int? userId,
    CancellationToken ct = default)
    {
        var ids = productVariantIds
            .Where(x => x > 0)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
            return;

        var variants = await _context.ProductVariants
            .Where(x => ids.Contains(x.Id) && !x.IsDeleted)
            .ToListAsync(ct);

        foreach (var variant in variants)
        {
            if (variant.HasInputInvoice)
                continue;

            variant.HasInputInvoice = true;
            variant.UpdatedAtUtc = DateTime.UtcNow;
            variant.UpdatedBy = userId;
        }
    }

}
