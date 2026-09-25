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

    private static IQueryable<PurchaseRequest> IncludeDetail(
      IQueryable<PurchaseRequest> query)
      => query
          .Include(request => request.Lines)
              .ThenInclude(line => line.ProductVariant)
                  .ThenInclude(variant => variant!.Product)
                      .ThenInclude(product => product.Supplier)

          .Include(request => request.Lines)
              .ThenInclude(line => line.ProductVariant)
                  .ThenInclude(variant => variant!.Product)
                      .ThenInclude(product => product.Tax)

          .Include(request => request.Lines)
              .ThenInclude(line => line.ProductUnitConversion)
                  .ThenInclude(conversion => conversion!.Unit)

          .Include(request => request.Actions)

          .Include(request => request.PurchaseOrders)
              .ThenInclude(order => order.Supplier)

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
        term = string.IsNullOrWhiteSpace(term) ? null : term.Trim();
        var openStatuses = new[]
        {
            PurchaseRequestStatus.PendingApproval,
            PurchaseRequestStatus.Approved,
            PurchaseRequestStatus.PartiallyConverted
        };
        var query = _db.ProductVariants.AsNoTracking().Where(x =>
            x.StoreId == storeId && x.IsActive && x.Product.IsActive &&
            x.UnitConversions.Any(c => c.IsActive));

        if (term != null)
        {
            query = query.Where(x =>
                x.Product.Name.Contains(term) ||
                (x.ProductVariantName != null && x.ProductVariantName.Contains(term)) ||
                (x.ProductVariantNameNormalized != null && x.ProductVariantNameNormalized.Contains(term)) ||
                x.Sku.Contains(term) ||
                x.UnitConversions.Any(c => c.IsActive && c.Barcodes.Any(b => b.IsActive && b.Barcode.Contains(term))) ||
                _db.ProductVariantBarcodeHistories.Any(h =>
                    h.StoreId == storeId && !h.IsDeleted && h.ProductVariantId == x.Id &&
                    (h.OldBarcode == term || h.NewBarcode == term)));
        }

        var ordered = term == null
            ? query.OrderBy(x => x.Product.Name).ThenBy(x => x.ProductVariantName)
            : query
                .OrderByDescending(x => x.UnitConversions.Any(c =>
                    c.IsActive && c.Barcodes.Any(b => b.IsActive && b.Barcode == term)))
                .ThenByDescending(x => x.Sku == term)
                .ThenByDescending(x => _db.ProductVariantBarcodeHistories.Any(h =>
                    h.StoreId == storeId && !h.IsDeleted && h.ProductVariantId == x.Id &&
                    (h.OldBarcode == term || h.NewBarcode == term)))
                .ThenBy(x => x.Product.Name)
                .ThenBy(x => x.ProductVariantName);

        var items = await ordered
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize + 1)
            .Select(x => new PurchaseRequestProductLookupDto
            {
                ProductVariantId = x.Id,
                Text = string.IsNullOrWhiteSpace(x.ProductVariantName)
                    ? x.Product.Name
                    : x.ProductVariantName!,
                Sku = x.Sku,
                ImageUrl = x.PrimaryProductImage != null
                    ? x.PrimaryProductImage.MediaAsset.StoragePath
                    : null,
                CurrentStockBaseQuantity = _db.InventoryBalances
                    .Where(b => b.ProductVariantId == x.Id)
                    .Sum(b => (decimal?)b.OnHandQty) ?? 0m,
                IncomingBaseQuantity = _db.PurchaseOrderLines
                    .Where(l => l.ProductVariantId == x.Id &&
                        (l.PurchaseOrder.Status == PurchaseOrderStatus.Approved ||
                         l.PurchaseOrder.Status == PurchaseOrderStatus.SentToSupplier ||
                         l.PurchaseOrder.Status == PurchaseOrderStatus.PartiallyReceived))
                    .Sum(l => (decimal?)((l.OrderedQuantity - l.ReceivedQuantity - l.ShortClosedQuantity) * l.ConversionFactor)) ?? 0m,
                HasOpenRequest = _db.PurchaseRequestLines.Any(l =>
                    l.ProductVariantId == x.Id && openStatuses.Contains(l.PurchaseRequest.Status)),
                OpenRequestCount = _db.PurchaseRequestLines
                    .Where(l => l.ProductVariantId == x.Id && openStatuses.Contains(l.PurchaseRequest.Status))
                    .Select(l => l.PurchaseRequestId)
                    .Distinct()
                    .Count(),
                OpenRequestBaseQuantity = _db.PurchaseRequestLines
                    .Where(l => l.ProductVariantId == x.Id && openStatuses.Contains(l.PurchaseRequest.Status))
                    .Sum(l => (decimal?)(l.RequestedQuantity * l.ConversionFactor)) ?? 0m,
                OpenRequestNumber = _db.PurchaseRequestLines
                    .Where(l => l.ProductVariantId == x.Id && openStatuses.Contains(l.PurchaseRequest.Status))
                    .OrderByDescending(l => l.PurchaseRequest.CreatedAtUtc)
                    .Select(l => l.PurchaseRequest.RequestNumber)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var hasMore = items.Count > pageSize;
        items = items.Take(pageSize).ToList();
        var variantIds = items.Select(x => x.ProductVariantId).ToArray();
        var unitOptions = await _db.ProductUnitConversions.AsNoTracking()
            .Where(x => x.StoreId == storeId && x.IsActive && variantIds.Contains(x.ProductVariantId))
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .Select(x => new
            {
                ProductVariantId = x.ProductVariantId,
                Option = new PurchaseRequestProductUnitOptionDto
                {
                    ProductUnitConversionId = x.Id,
                    UnitId = x.UnitId,
                    UnitName = x.Unit.Name,
                    Factor = x.Factor,
                    Barcode = x.Barcodes.Where(b => b.IsActive)
                        .OrderByDescending(b => term != null && b.Barcode == term)
                        .ThenByDescending(b => b.IsPrimary)
                        .ThenBy(b => b.Id)
                        .Select(b => b.Barcode)
                        .FirstOrDefault(),
                    IsBaseUnit = x.IsBaseUnit,
                    IsDefaultForSale = x.IsDefaultForSale,
                    IsBarcodeMatch = term != null && x.Barcodes.Any(b => b.IsActive && b.Barcode == term)
                }
            })
            .ToListAsync(ct);

        HashSet<int> historicalMatches = new();
        if (term != null && variantIds.Length > 0)
        {
            historicalMatches = (await _db.ProductVariantBarcodeHistories.AsNoTracking()
                .Where(x => x.StoreId == storeId && !x.IsDeleted && variantIds.Contains(x.ProductVariantId) &&
                    (x.OldBarcode == term || x.NewBarcode == term))
                .Select(x => x.ProductUnitConversionId)
                .Distinct()
                .ToListAsync(ct)).ToHashSet();
        }

        var optionsByVariant = unitOptions
            .GroupBy(x => x.ProductVariantId)
            .ToDictionary(x => x.Key, x => x.Select(y => y.Option).ToList());
        foreach (var item in items)
        {
            item.UnitOptions = optionsByVariant.GetValueOrDefault(item.ProductVariantId) ?? new();
            foreach (var option in item.UnitOptions)
                option.IsHistoricalBarcodeMatch = historicalMatches.Contains(option.ProductUnitConversionId) && !option.IsBarcodeMatch;

            var selected = item.UnitOptions
                .OrderByDescending(x => x.IsBarcodeMatch)
                .ThenByDescending(x => x.IsHistoricalBarcodeMatch)
                .ThenByDescending(x => x.IsDefaultForSale)
                .ThenByDescending(x => x.IsBaseUnit)
                .FirstOrDefault();
            if (selected == null) continue;
            item.ProductUnitConversionId = selected.ProductUnitConversionId;
            item.UnitId = selected.UnitId;
            item.UnitName = selected.UnitName;
            item.Factor = selected.Factor;
            item.Barcode = selected.Barcode;
        }

        return new PurchaseLookupPageDto<PurchaseRequestProductLookupDto>
        {
            HasMore = hasMore,
            Items = items
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
