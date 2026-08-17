using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Purchases;
using GaoApp.Application.Interfaces.Repositories.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Purchases;

public sealed class PurchaseOrderRepository : IPurchaseOrderRepository
{
    private readonly AppDbContext _db;
    public PurchaseOrderRepository(AppDbContext db) => _db = db;

    public async Task<PagedResult<PurchaseOrder>> SearchAsync(
        string? search,
        int? legalEntityId,
        DateTime? fromDate,
        DateTime? toDate,
        IReadOnlyCollection<PurchaseOrderStatus>? statuses,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        IQueryable<PurchaseOrder> query = _db.PurchaseOrders.AsNoTracking();

        search = search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(x =>
                x.OrderNumber.Contains(search) ||
                (x.Title != null && x.Title.Contains(search)) ||
                x.Supplier.Name.Contains(search) ||
                x.Lines.Any(line =>
                    !line.IsDeleted &&
                    (line.ProductNameSnapshot.Contains(search) ||
                     (line.SkuSnapshot != null && line.SkuSnapshot.Contains(search)))));
        }
        if (legalEntityId.HasValue)
            query = query.Where(x => x.LegalEntityId == legalEntityId.Value);
        if (fromDate.HasValue)
            query = query.Where(x => x.OrderDate >= fromDate.Value.Date);
        if (toDate.HasValue)
        {
            var exclusiveEnd = toDate.Value.Date.AddDays(1);
            query = query.Where(x => x.OrderDate < exclusiveEnd);
        }
        if (statuses is { Count: > 0 })
            query = query.Where(x => statuses.Contains(x.Status));

        var total = await query.CountAsync(ct);
        var items = await query
            .Include(x => x.Supplier)
            .Include(x => x.ExpectedWarehouse)
            .Include(x => x.LegalEntity)
            .Include(x => x.Lines)
            .OrderByDescending(x => x.OrderDate)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsSplitQuery()
            .ToListAsync(ct);

        return new PagedResult<PurchaseOrder>(page, pageSize, total, items);
    }

    public Task<Dictionary<PurchaseOrderStatus, int>> GetStatusCountsAsync(CancellationToken ct = default)
        => _db.PurchaseOrders.AsNoTracking()
            .GroupBy(x => x.Status)
            .Select(x => new { Status = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);

    public Task<int> CountPendingApprovalAsync(CancellationToken ct = default)
        => _db.PurchaseOrders.AsNoTracking().CountAsync(
            x => x.Status == PurchaseOrderStatus.PendingApproval, ct);

    public Task<PurchaseOrder?> GetDetailAsync(int id, bool tracking = false, CancellationToken ct = default)
    {
        IQueryable<PurchaseOrder> query = _db.PurchaseOrders;
        if (!tracking) query = query.AsNoTracking();
        return query.Include(x => x.Supplier).Include(x => x.ExpectedWarehouse).Include(x => x.LegalEntity)
            .Include(x => x.Lines).ThenInclude(x => x.SourcePurchaseRequestLine)
            .Include(x => x.Lines).ThenInclude(x => x.ProductVariant).ThenInclude(x => x!.Product)
            .Include(x => x.Lines).ThenInclude(x => x.ProductVariant).ThenInclude(x => x!.Product).ThenInclude(x => x.BaseUnit)
            .Include(x => x.Lines).ThenInclude(x => x.ProductVariant).ThenInclude(x => x!.UnitConversions).ThenInclude(x => x.Unit)
            .Include(x => x.Lines).ThenInclude(x => x.ProductUnitConversion).ThenInclude(x => x!.Unit)
            .Include(x => x.SourcePurchaseRequest).ThenInclude(x => x!.Lines)
            .Include(x => x.Actions).Include(x => x.Receipts)
            .AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<bool> LockForOutstandingManagementAsync(
        int storeId,
        int purchaseOrderId,
        CancellationToken ct = default)
    {
        if (_db.Database.CurrentTransaction == null)
            throw new InvalidOperationException(
                "Purchase-order outstanding management requires an active transaction.");

        var lockedId = await _db.PurchaseOrders
            .FromSqlInterpolated($@"SELECT po.* FROM [PurchaseOrders] po WITH (UPDLOCK,HOLDLOCK,ROWLOCK)
                WHERE po.[Id] = {purchaseOrderId} AND po.[StoreId] = {storeId} AND po.[IsDeleted] = 0")
            .AsNoTracking()
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(ct);
        return lockedId.HasValue;
    }

    public Task<bool> HasActiveReceiptLinesAsync(
        int storeId,
        int purchaseOrderId,
        IReadOnlyCollection<int> purchaseOrderLineIds,
        CancellationToken ct = default)
    {
        if (purchaseOrderLineIds.Count == 0) return Task.FromResult(false);
        return _db.StockDocumentLines.AsNoTracking().AnyAsync(x =>
            x.PurchaseOrderLineId.HasValue && purchaseOrderLineIds.Contains(x.PurchaseOrderLineId.Value) &&
            !x.IsDeleted && !x.StockDocument.IsDeleted &&
            x.StockDocument.StoreId == storeId &&
            x.StockDocument.PurchaseOrderId == purchaseOrderId &&
            (x.StockDocument.Status == StockDocumentStatus.Draft ||
             x.StockDocument.Status == StockDocumentStatus.PendingApproval ||
             x.StockDocument.Status == StockDocumentStatus.Rejected), ct);
    }

    public async Task<IReadOnlyDictionary<int, decimal>> GetInFlightReceiptQuantitiesAsync(
        int purchaseOrderId, IReadOnlyCollection<int> purchaseOrderLineIds,
        CancellationToken ct = default)
    {
        if (purchaseOrderLineIds.Count == 0) return new Dictionary<int, decimal>();
        var storeId = _db.CurrentStoreId;
        if (!storeId.HasValue || storeId.Value <= 0) return new Dictionary<int, decimal>();
        return await _db.StockDocumentLines.AsNoTracking()
            .Where(x => x.PurchaseOrderLineId.HasValue && purchaseOrderLineIds.Contains(x.PurchaseOrderLineId.Value) &&
                !x.IsDeleted && !x.StockDocument.IsDeleted && x.StockDocument.StoreId == storeId.Value &&
                x.StockDocument.PurchaseOrderId == purchaseOrderId &&
                (x.StockDocument.Status == StockDocumentStatus.Draft ||
                 x.StockDocument.Status == StockDocumentStatus.PendingApproval ||
                 x.StockDocument.Status == StockDocumentStatus.Rejected))
            .GroupBy(x => x.PurchaseOrderLineId!.Value)
            .Select(x => new { Id = x.Key, Quantity = x.Sum(y => y.BaseQuantity) })
            .ToDictionaryAsync(x => x.Id, x => x.Quantity, ct);
    }

    public async Task<Dictionary<int, string>> GetUserDisplayNamesAsync(
        IEnumerable<int> userIds,
        CancellationToken ct = default)
    {
        var ids = userIds.Where(x => x > 0).Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<int, string>();

        return await _db.Users.AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(
                x => x.Id,
                x => string.IsNullOrWhiteSpace(x.FullName) ? x.UserName : x.FullName!,
                ct);
    }

    public Task AddAsync(PurchaseOrder entity, CancellationToken ct = default)
        => _db.PurchaseOrders.AddAsync(entity, ct).AsTask();
    public Task<Supplier?> GetSupplierAsync(int id, CancellationToken ct = default)
        => _db.Suppliers.FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
    public Task<Warehouse?> GetWarehouseAsync(int id, CancellationToken ct = default)
        => _db.Warehouses.Include(x => x.LegalEntity).FirstOrDefaultAsync(x => x.Id == id, ct);
    public Task<LegalEntity?> GetLegalEntityAsync(int id, CancellationToken ct = default)
        => _db.LegalEntities.FirstOrDefaultAsync(x => x.Id == id, ct);
    public Task<ProductVariant?> GetVariantAsync(int id, CancellationToken ct = default)
        => _db.ProductVariants.Include(x => x.Product).ThenInclude(x => x.Tax)
            .FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
    public Task<ProductUnitConversion?> GetConversionAsync(int id, CancellationToken ct = default)
        => _db.ProductUnitConversions.Include(x => x.Unit).FirstOrDefaultAsync(x => x.Id == id, ct);
    public Task<ProductUnitConversion?> GetDefaultProductConversionAsync(
        int productId,
        CancellationToken ct = default)
        => _db.ProductUnitConversions
            .Include(x => x.Unit)
            .Include(x => x.ProductVariant).ThenInclude(x => x.Product)
            .Where(x => x.ProductVariant.ProductId == productId &&
                        x.ProductVariant.IsActive &&
                        x.ProductVariant.Product.IsActive &&
                        x.IsActive)
            .OrderByDescending(x => x.IsBaseUnit)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .FirstOrDefaultAsync(ct);
    public Task<Category?> GetCategoryAsync(int id, CancellationToken ct = default)
        => _db.Categories.FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
    public Task<Unit?> GetUnitAsync(int id, CancellationToken ct = default)
        => _db.Units.FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
    public Task<List<Category>> GetActiveCategoriesAsync(CancellationToken ct = default)
        => _db.Categories.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync(ct);
    public Task<List<Unit>> GetActiveUnitsAsync(CancellationToken ct = default)
        => _db.Units.AsNoTracking().Where(x => x.IsActive)
            .OrderByDescending(x => x.IsBase).ThenBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync(ct);
    public Task<bool> HasReceiptLineAsync(int purchaseOrderLineId, CancellationToken ct = default)
        => _db.StockDocumentLines.AnyAsync(x => x.PurchaseOrderLineId == purchaseOrderLineId, ct);
    public Task<Tax?> GetTaxAsync(int id, CancellationToken ct = default)
        => _db.Taxes.FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
    public Task<List<Supplier>> GetSuppliersByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return Task.FromResult(new List<Supplier>());
        return _db.Suppliers.AsNoTracking().Where(x => ids.Contains(x.Id)).OrderBy(x => x.Name).ToListAsync(ct);
    }
    public Task<List<Warehouse>> GetWarehousesAsync(CancellationToken ct = default)
        => _db.Warehouses.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
    public Task<List<LegalEntity>> GetLegalEntitiesAsync(CancellationToken ct = default)
        => _db.LegalEntities.AsNoTracking().OrderBy(x => x.SalePriority).ThenBy(x => x.Code).ToListAsync(ct);
    public Task<List<Tax>> GetTaxesAsync(CancellationToken ct = default)
        => _db.Taxes.AsNoTracking().OrderBy(x => x.Rate).ToListAsync(ct);
    public Task<List<ProductUnitConversion>> GetProductOptionsByIdsAsync(
        IReadOnlyCollection<int> ids,
        CancellationToken ct = default)
    {
        if (ids.Count == 0) return Task.FromResult(new List<ProductUnitConversion>());
        return _db.ProductUnitConversions.AsNoTracking()
            .Include(x => x.Unit)
            .Include(x => x.Barcodes)
            .Include(x => x.ProductVariant).ThenInclude(x => x.Product).ThenInclude(x => x.Tax)
            .Include(x => x.ProductVariant).ThenInclude(x => x.Product).ThenInclude(x => x.Supplier)
            .Include(x => x.ProductVariant).ThenInclude(x => x.PrimaryProductImage).ThenInclude(x => x!.MediaAsset)
            .Where(x => ids.Contains(x.Id))
            .OrderBy(x => x.ProductVariant.Product.Name).ThenBy(x => x.SortOrder)
            .AsSplitQuery()
            .ToListAsync(ct);
    }

    public async Task<PurchaseLookupPageDto<PurchaseSupplierLookupDto>> SearchSuppliersAsync(
        int storeId,
        string? term,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var query = _db.Suppliers.AsNoTracking()
            .Where(x => x.StoreId == storeId && x.IsActive);

        if (!string.IsNullOrWhiteSpace(term))
        {
            query = query.Where(x =>
                x.Name.Contains(term) ||
                x.Code.Contains(term) ||
                (x.Phone != null && x.Phone.Contains(term)) ||
                (x.TaxCode != null && x.TaxCode.Contains(term)));
        }

        var items = await query
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize + 1)
            .Select(x => new PurchaseSupplierLookupDto
            {
                Id = x.Id,
                Text = x.Name,
                Code = x.Code,
                Phone = x.Phone,
                TaxCode = x.TaxCode
            })
            .ToListAsync(ct);

        return new PurchaseLookupPageDto<PurchaseSupplierLookupDto>
        {
            HasMore = items.Count > pageSize,
            Items = items.Take(pageSize).ToList()
        };
    }

    public async Task<PurchaseLookupPageDto<PurchaseProductLookupDto>> SearchProductsAsync(
        int storeId,
        string? term,
        int? preferredSupplierId,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var query = _db.ProductUnitConversions.AsNoTracking()
            .Where(x =>
                x.StoreId == storeId &&
                x.IsActive &&
                x.ProductVariant.IsActive &&
                x.ProductVariant.Product.IsActive);

        if (!string.IsNullOrWhiteSpace(term))
        {
            query = query.Where(x =>
                x.ProductVariant.Product.Name.Contains(term) ||
                (x.ProductVariant.ProductVariantName != null && x.ProductVariant.ProductVariantName.Contains(term)) ||
                (x.ProductVariant.ProductVariantNameNormalized != null && x.ProductVariant.ProductVariantNameNormalized.Contains(term)) ||
                x.ProductVariant.Sku.Contains(term) ||
                x.Barcodes.Any(b => b.IsActive && b.Barcode.Contains(term)));
        }

        var ordered = query
            .OrderByDescending(x => preferredSupplierId.HasValue &&
                                    x.ProductVariant.Product.SupplierId == preferredSupplierId.Value)
            .ThenBy(x => x.ProductVariant.Product.Name)
            .ThenBy(x => x.ProductVariant.ProductVariantName)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.Id);

        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize + 1)
            .Select(x => new PurchaseProductLookupDto
            {
                ProductVariantId = x.ProductVariantId,
                ProductUnitConversionId = x.Id,
                UnitId = x.UnitId,
                ProductName = string.IsNullOrWhiteSpace(x.ProductVariant.ProductVariantName)
                    ? x.ProductVariant.Product.Name
                    : x.ProductVariant.ProductVariantName!,
                Text = string.IsNullOrWhiteSpace(x.ProductVariant.ProductVariantName)
                    ? x.ProductVariant.Product.Name
                    : x.ProductVariant.ProductVariantName!,
                Sku = x.ProductVariant.Sku,
                UnitName = x.Unit.Name,
                Factor = x.Factor,
                Barcode = x.Barcodes
                    .Where(b => b.IsActive)
                    .OrderByDescending(b => b.IsPrimary)
                    .ThenBy(b => b.Id)
                    .Select(b => b.Barcode)
                    .FirstOrDefault(),
                ImageUrl = x.ProductVariant.PrimaryProductImage != null
                    ? x.ProductVariant.PrimaryProductImage.MediaAsset.StoragePath
                    : null,
                DefaultTaxId = x.ProductVariant.Product.Tax != null && x.ProductVariant.Product.Tax.IsActive
                    ? x.ProductVariant.Product.TaxId
                    : null,
                DefaultTaxRate = x.ProductVariant.Product.Tax != null && x.ProductVariant.Product.Tax.IsActive
                    ? x.ProductVariant.Product.Tax.Rate
                    : 0m,
                SuggestedUnitPriceAfterVat = x.ProductVariant.CostPrice * x.Factor,
                SupplierId = x.ProductVariant.Product.SupplierId,
                SupplierName = x.ProductVariant.Product.Supplier.Name,
                SupplierMatch = preferredSupplierId.HasValue &&
                                x.ProductVariant.Product.SupplierId == preferredSupplierId.Value
            })
            .ToListAsync(ct);

        return new PurchaseLookupPageDto<PurchaseProductLookupDto>
        {
            HasMore = items.Count > pageSize,
            Items = items.Take(pageSize).ToList()
        };
    }

    public async Task<int> GetNextLineNumberAsync(int purchaseOrderId, CancellationToken ct = default)
    {
        var maxLineNo = await _db.PurchaseOrderLines
            .IgnoreQueryFilters()
            .Where(x => x.PurchaseOrderId == purchaseOrderId)
            .Select(x => (int?)x.LineNo)
            .MaxAsync(ct) ?? 0;

        return checked(maxLineNo + 1);
    }

    public Task RemoveLineAsync(PurchaseOrderLine line, CancellationToken ct = default)
    {
        _db.PurchaseOrderLines.Remove(line);
        return Task.CompletedTask;
    }
    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
