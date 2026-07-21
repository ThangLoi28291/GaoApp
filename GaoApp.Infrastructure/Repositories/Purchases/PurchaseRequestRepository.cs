using GaoApp.Application.DTOs.Purchases;
using GaoApp.Application.Interfaces.Repositories.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Purchases;

public sealed class PurchaseRequestRepository : IPurchaseRequestRepository
{
    private readonly AppDbContext _db;

    public PurchaseRequestRepository(AppDbContext db) => _db = db;

    public Task<List<PurchaseRequest>> GetListAsync(int? requestedByUserId, CancellationToken ct = default)
    {
        var query = _db.PurchaseRequests.AsNoTracking().Include(x => x.Lines).AsQueryable();
        if (requestedByUserId.HasValue)
            query = query.Where(x => x.RequestedByUserId == requestedByUserId.Value);
        return query.OrderByDescending(x => x.RequestDate).ThenByDescending(x => x.Id).ToListAsync(ct);
    }

    public Task<PurchaseRequest?> GetDetailAsync(int id, bool tracking = false, CancellationToken ct = default)
    {
        IQueryable<PurchaseRequest> query = _db.PurchaseRequests;
        if (!tracking) query = query.AsNoTracking();
        return IncludeDetail(query)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public Task<PurchaseRequest?> GetDetailForConversionAsync(
        int id,
        int storeId,
        CancellationToken ct = default)
    {
        // Convert chạy trong transaction. UPDLOCK khóa đúng header request cho tới commit,
        // nhờ đó hai request với hai idempotency key khác nhau không thể đồng thời tạo
        // hai PO đang hoạt động cho cùng một yêu cầu.
        var query = _db.PurchaseRequests.FromSqlInterpolated($@"
            SELECT *
            FROM [PurchaseRequests] WITH (UPDLOCK, HOLDLOCK)
            WHERE [Id] = {id} AND [StoreId] = {storeId}");
        return IncludeDetail(query)
            .FirstOrDefaultAsync(x => x.Id == id && x.StoreId == storeId, ct);
    }

    private static IQueryable<PurchaseRequest> IncludeDetail(IQueryable<PurchaseRequest> query)
        => query
            .Include(x => x.Lines).ThenInclude(x => x.ProductVariant).ThenInclude(x => x.Product).ThenInclude(x => x.Supplier)
            .Include(x => x.Lines).ThenInclude(x => x.ProductVariant).ThenInclude(x => x.Product).ThenInclude(x => x.Tax)
            .Include(x => x.Lines).ThenInclude(x => x.ProductUnitConversion).ThenInclude(x => x.Unit)
            .Include(x => x.Actions)
            .Include(x => x.PurchaseOrders).ThenInclude(x => x.Supplier)
            .AsSplitQuery();

    public Task AddAsync(PurchaseRequest entity, CancellationToken ct = default)
        => _db.PurchaseRequests.AddAsync(entity, ct).AsTask();

    public Task AddPurchaseOrderAsync(PurchaseOrder entity, CancellationToken ct = default)
        => _db.PurchaseOrders.AddAsync(entity, ct).AsTask();

    public Task<PurchaseOrder?> GetPurchaseOrderByConversionKeyAsync(
        int purchaseRequestId,
        string conversionKey,
        bool tracking = false,
        CancellationToken ct = default)
    {
        IQueryable<PurchaseOrder> query = _db.PurchaseOrders;
        if (!tracking) query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(x =>
            x.SourcePurchaseRequestId == purchaseRequestId &&
            x.SourceConversionKey == conversionKey, ct);
    }

    public async Task<int> GetNextLineNumberAsync(int purchaseRequestId, CancellationToken ct = default)
    {
        var max = await _db.PurchaseRequestLines.IgnoreQueryFilters()
            .Where(x => x.PurchaseRequestId == purchaseRequestId)
            .Select(x => (int?)x.LineNo)
            .MaxAsync(ct) ?? 0;
        return checked(max + 1);
    }

    public Task RemoveLineAsync(PurchaseRequestLine line, CancellationToken ct = default)
    {
        _db.PurchaseRequestLines.Remove(line);
        return Task.CompletedTask;
    }

    public Task<ProductVariant?> GetVariantAsync(int id, CancellationToken ct = default)
        => _db.ProductVariants
            .Include(x => x.Product).ThenInclude(x => x.Supplier)
            .Include(x => x.Product).ThenInclude(x => x.Tax)
            .FirstOrDefaultAsync(x => x.Id == id && x.IsActive && x.Product.IsActive, ct);

    public Task<ProductUnitConversion?> GetConversionAsync(int id, CancellationToken ct = default)
        => _db.ProductUnitConversions.Include(x => x.Unit)
            .FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct);

    public Task<Supplier?> GetSupplierAsync(int id, CancellationToken ct = default)
        => _db.Suppliers.FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct);

    public Task<Warehouse?> GetWarehouseAsync(int id, CancellationToken ct = default)
        => _db.Warehouses.Include(x => x.LegalEntity)
            .FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct);

    public Task<LegalEntity?> GetLegalEntityAsync(int id, CancellationToken ct = default)
        => _db.LegalEntities.FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct);

    public Task<Tax?> GetTaxAsync(int id, CancellationToken ct = default)
        => _db.Taxes.FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct);

    public Task<List<Warehouse>> GetWarehousesAsync(CancellationToken ct = default)
        => _db.Warehouses.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(ct);

    public Task<List<LegalEntity>> GetLegalEntitiesAsync(CancellationToken ct = default)
        => _db.LegalEntities.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.SalePriority).ThenBy(x => x.Code).ToListAsync(ct);

    public Task<List<Tax>> GetTaxesAsync(CancellationToken ct = default)
        => _db.Taxes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Rate).ToListAsync(ct);

    public async Task<Dictionary<int, string>> GetUserDisplayNamesAsync(
        IEnumerable<int> userIds,
        CancellationToken ct = default)
    {
        var ids = userIds.Where(x => x > 0).Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<int, string>();
        return await _db.Users.AsNoTracking().Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => string.IsNullOrWhiteSpace(x.FullName) ? x.UserName : x.FullName!, ct);
    }

    public async Task<Dictionary<int, PurchaseRequestInventoryContextDto>> GetInventoryContextAsync(
        int storeId,
        IReadOnlyCollection<int> productVariantIds,
        CancellationToken ct = default)
    {
        var ids = productVariantIds.Where(x => x > 0).Distinct().ToArray();
        if (ids.Length == 0)
            return new Dictionary<int, PurchaseRequestInventoryContextDto>();

        // Một query theo toàn bộ variant của chứng từ, tránh N+1. Các subquery đều
        // có StoreId tường minh ngoài global filters để không thể cộng chéo cửa hàng.
        var rows = await _db.ProductVariants.AsNoTracking()
            .Where(v => v.StoreId == storeId && ids.Contains(v.Id))
            .Select(v => new PurchaseRequestInventoryContextDto
            {
                ProductVariantId = v.Id,
                CurrentStockBaseQuantity = _db.InventoryBalances
                    .Where(b => b.StoreId == storeId && b.ProductVariantId == v.Id)
                    .Sum(b => (decimal?)b.OnHandQty) ?? 0m,
                IncomingBaseQuantity = _db.PurchaseOrderLines
                    .Where(l =>
                        l.StoreId == storeId &&
                        l.PurchaseOrder.StoreId == storeId &&
                        l.ProductVariantId == v.Id &&
                        (l.PurchaseOrder.Status == PurchaseOrderStatus.Approved ||
                         l.PurchaseOrder.Status == PurchaseOrderStatus.SentToSupplier ||
                         l.PurchaseOrder.Status == PurchaseOrderStatus.PartiallyReceived))
                    .Sum(l => (decimal?)(
                        (l.OrderedQuantity > l.ReceivedQuantity + l.ShortClosedQuantity
                            ? l.OrderedQuantity - l.ReceivedQuantity - l.ShortClosedQuantity
                            : 0m) * l.ConversionFactor)) ?? 0m
            })
            .ToListAsync(ct);

        return rows.ToDictionary(x => x.ProductVariantId);
    }

    public async Task<PurchaseLookupPageDto<PurchaseRequestProductLookupDto>> SearchProductsAsync(
        int storeId,
        string? term,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var query = _db.ProductUnitConversions.AsNoTracking().Where(x =>
            x.StoreId == storeId && x.IsActive && x.ProductVariant.IsActive && x.ProductVariant.Product.IsActive);

        if (!string.IsNullOrWhiteSpace(term))
        {
            query = query.Where(x =>
                x.ProductVariant.Product.Name.Contains(term) ||
                (x.ProductVariant.ProductVariantName != null && x.ProductVariant.ProductVariantName.Contains(term)) ||
                (x.ProductVariant.ProductVariantNameNormalized != null && x.ProductVariant.ProductVariantNameNormalized.Contains(term)) ||
                x.ProductVariant.Sku.Contains(term) ||
                x.Barcodes.Any(b => b.IsActive && b.Barcode.Contains(term)));
        }

        var items = await query
            .OrderBy(x => x.ProductVariant.Product.Name)
            .ThenBy(x => x.ProductVariant.ProductVariantName)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize + 1)
            .Select(x => new PurchaseRequestProductLookupDto
            {
                ProductVariantId = x.ProductVariantId,
                ProductUnitConversionId = x.Id,
                UnitId = x.UnitId,
                Text = string.IsNullOrWhiteSpace(x.ProductVariant.ProductVariantName)
                    ? x.ProductVariant.Product.Name
                    : x.ProductVariant.ProductVariantName!,
                Sku = x.ProductVariant.Sku,
                UnitName = x.Unit.Name,
                Factor = x.Factor,
                Barcode = x.Barcodes.Where(b => b.IsActive).OrderByDescending(b => b.IsPrimary)
                    .ThenBy(b => b.Id).Select(b => b.Barcode).FirstOrDefault(),
                ImageUrl = x.ProductVariant.PrimaryProductImage != null
                    ? x.ProductVariant.PrimaryProductImage.MediaAsset.StoragePath
                    : null,
                CurrentStockBaseQuantity = _db.InventoryBalances
                    .Where(b => b.ProductVariantId == x.ProductVariantId)
                    .Sum(b => (decimal?)b.OnHandQty) ?? 0m,
                IncomingBaseQuantity = _db.PurchaseOrderLines
                    .Where(l => l.ProductVariantId == x.ProductVariantId &&
                        (l.PurchaseOrder.Status == PurchaseOrderStatus.Approved ||
                         l.PurchaseOrder.Status == PurchaseOrderStatus.SentToSupplier ||
                         l.PurchaseOrder.Status == PurchaseOrderStatus.PartiallyReceived))
                    .Sum(l => (decimal?)((l.OrderedQuantity - l.ReceivedQuantity - l.ShortClosedQuantity) * l.ConversionFactor)) ?? 0m,
                HasOpenRequest = _db.PurchaseRequestLines.Any(l =>
                    l.ProductVariantId == x.ProductVariantId &&
                    (l.PurchaseRequest.Status == PurchaseRequestStatus.PendingApproval ||
                     l.PurchaseRequest.Status == PurchaseRequestStatus.Approved ||
                     l.PurchaseRequest.Status == PurchaseRequestStatus.PartiallyConverted))
            })
            .ToListAsync(ct);

        return new PurchaseLookupPageDto<PurchaseRequestProductLookupDto>
        {
            HasMore = items.Count > pageSize,
            Items = items.Take(pageSize).ToList()
        };
    }

    public Task<List<ProductUnitConversion>> GetProductOptionsByIdsAsync(
        IReadOnlyCollection<int> ids,
        CancellationToken ct = default)
    {
        if (ids.Count == 0) return Task.FromResult(new List<ProductUnitConversion>());
        return _db.ProductUnitConversions.AsNoTracking()
            .Include(x => x.Unit).Include(x => x.Barcodes)
            .Include(x => x.ProductVariant).ThenInclude(x => x.Product).ThenInclude(x => x.Supplier)
            .Include(x => x.ProductVariant).ThenInclude(x => x.Product).ThenInclude(x => x.Tax)
            .Include(x => x.ProductVariant).ThenInclude(x => x.PrimaryProductImage).ThenInclude(x => x!.MediaAsset)
            .Where(x => ids.Contains(x.Id)).AsSplitQuery().ToListAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
