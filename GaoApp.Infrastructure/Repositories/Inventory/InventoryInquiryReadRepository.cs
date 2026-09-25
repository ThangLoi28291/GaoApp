using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Inventory;

public sealed class InventoryInquiryReadRepository : IInventoryInquiryReadRepository
{
    private const string AccentInsensitiveSearchCollation =
        "Latin1_General_100_CI_AI";

    private readonly AppDbContext _db;

    public InventoryInquiryReadRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<InventoryInquiryPageDto> QueryAsync(
        int storeId,
        InventoryInquiryQueryRequest request,
        int? costViewerUserId,
        CancellationToken ct = default)
    {
        var query = BuildBaseQuery(storeId);

        if (request.WarehouseId.HasValue)
            query = query.Where(x => x.WarehouseId == request.WarehouseId.Value);

        query = ApplyKeyword(query, storeId, request.Keyword);

        var summary = await query
            .GroupBy(_ => 1)
            .Select(group => new InventoryInquirySummaryDto
            {
                TotalItems = group.Count(),
                NegativeItems = group.Count(x => x.OnHandQty < 0),
                ZeroItems = group.Count(x => x.OnHandQty == 0),
                PositiveItems = group.Count(x => x.OnHandQty > 0)
            })
            .FirstOrDefaultAsync(ct)
            ?? new InventoryInquirySummaryDto();

        query = ApplyState(query, request.State);
        query = ApplySort(query, request.SortBy, request.SortDirection);

        var totalItems = await query.CountAsync(ct);
        var totalPages = Math.Max(
            1,
            (int)Math.Ceiling((double)totalItems / request.PageSize));
        var page = Math.Min(request.Page, totalPages);

        var rows = await query
            .Skip((page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(balance => new
            {
                balance.Id,
                balance.WarehouseId,
                balance.ProductVariantId,
                WarehouseName = balance.Warehouse.Name,
                ProductName = balance.ProductVariant.Product.Name,
                VariantName = balance.ProductVariant.ProductVariantName,
                VariantImagePath = balance.ProductVariant.PrimaryProductImage != null
                    && !balance.ProductVariant.PrimaryProductImage.IsDeleted
                        ? balance.ProductVariant.PrimaryProductImage.MediaAsset.StoragePath
                        : null,
                ProductImagePath = balance.ProductVariant.Product.ProductImages
                    .Where(image => !image.IsDeleted)
                    .OrderByDescending(image => image.IsPrimary)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.Id)
                    .Select(image => image.MediaAsset.StoragePath)
                    .FirstOrDefault(),
                Barcode = balance.ProductVariant.UnitConversions
                    .Where(conversion =>
                        conversion.StoreId == storeId
                        && !conversion.IsDeleted
                        && conversion.IsActive)
                    .SelectMany(conversion => conversion.Barcodes
                        .Where(barcode =>
                            barcode.StoreId == storeId
                            && !barcode.IsDeleted
                            && barcode.IsActive)
                        .Select(barcode => new
                        {
                            Conversion = conversion,
                            Barcode = barcode
                        }))
                    .OrderByDescending(item => item.Conversion.IsDefaultForSale)
                    .ThenByDescending(item => item.Conversion.IsBaseUnit)
                    .ThenByDescending(item => item.Barcode.IsPrimary)
                    .ThenBy(item => item.Barcode.Id)
                    .Select(item => item.Barcode.Barcode)
                    .FirstOrDefault(),
                balance.OnHandQty,
                balance.ReservedQty
            })
            .ToListAsync(ct);

        var canViewCost = await InventoryCostReadAccess.CanViewAsync(_db, storeId, costViewerUserId, ct);
        var costs = new Dictionary<(int WarehouseId, int ProductVariantId), InventoryInquiryCostDto>();
        if (canViewCost && rows.Count > 0)
        {
            var balanceIds = rows.Select(row => row.Id).ToArray();
            costs = (await BuildCostQuery(BuildBaseQuery(storeId)
                    .Where(balance => balanceIds.Contains(balance.Id)), storeId).ToListAsync(ct))
                .ToDictionary(cost => (cost.WarehouseId, cost.ProductVariantId));
        }

        return new InventoryInquiryPageDto
        {
            CanViewCost = canViewCost,
            Page = page,
            PageSize = request.PageSize,
            TotalItems = totalItems,
            Summary = summary,
            Items = rows.Select(row => new InventoryInquiryListItemDto
            {
                Cost = costs.GetValueOrDefault((row.WarehouseId, row.ProductVariantId)),
                WarehouseId = row.WarehouseId,
                ProductVariantId = row.ProductVariantId,
                WarehouseName = row.WarehouseName,
                ProductName = row.ProductName,
                VariantName = row.VariantName,
                ImageUrl = NormalizeImageUrl(
                    row.VariantImagePath ?? row.ProductImagePath),
                Barcode = row.Barcode,
                OnHandQty = row.OnHandQty,
                ReservedQty = row.ReservedQty,
                AvailableQty = row.OnHandQty - row.ReservedQty,
                State = ResolveState(row.OnHandQty)
            }).ToList()
        };
    }

    public async Task<InventoryInquiryQuickViewDto?> GetQuickViewAsync(
        int storeId,
        int warehouseId,
        int productVariantId,
        int? costViewerUserId,
        CancellationToken ct = default)
    {
        var balance = await _db.InventoryBalances
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.ProductVariant)
                .ThenInclude(variant => variant.PrimaryProductImage)
                    .ThenInclude(image => image!.MediaAsset)
            .Include(x => x.ProductVariant)
                .ThenInclude(variant => variant.Product)
                    .ThenInclude(product => product.BaseUnit)
            .Include(x => x.ProductVariant)
                .ThenInclude(variant => variant.Product)
                    .ThenInclude(product => product.ProductImages)
                        .ThenInclude(image => image.MediaAsset)
            .Include(x => x.ProductVariant)
                .ThenInclude(variant => variant.UnitConversions
                    .Where(conversion => !conversion.IsDeleted && conversion.IsActive))
                    .ThenInclude(conversion => conversion.Unit)
            .Include(x => x.ProductVariant)
                .ThenInclude(variant => variant.UnitConversions
                    .Where(conversion => !conversion.IsDeleted && conversion.IsActive))
                    .ThenInclude(conversion => conversion.Barcodes
                        .Where(barcode => !barcode.IsDeleted && barcode.IsActive))
            .FirstOrDefaultAsync(x =>
                x.StoreId == storeId
                && !x.IsDeleted
                && x.WarehouseId == warehouseId
                && x.Warehouse.StoreId == storeId
                && !x.Warehouse.IsDeleted
                && x.ProductVariantId == productVariantId
                && x.ProductVariant.StoreId == storeId
                && !x.ProductVariant.IsDeleted
                && x.ProductVariant.Product.StoreId == storeId
                && !x.ProductVariant.Product.IsDeleted,
                ct);

        if (balance is null)
            return null;

        var variant = balance.ProductVariant;
        var product = variant.Product;
        var variantImagePath = variant.PrimaryProductImage is { IsDeleted: false }
            ? variant.PrimaryProductImage.MediaAsset?.StoragePath
            : null;
        var productImagePath = product.ProductImages
            .Where(image => !image.IsDeleted)
            .OrderByDescending(image => image.IsPrimary)
            .ThenBy(image => image.SortOrder)
            .ThenBy(image => image.Id)
            .Select(image => image.MediaAsset?.StoragePath)
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));

        var units = variant.UnitConversions
            .Where(conversion => !conversion.IsDeleted && conversion.IsActive)
            .OrderByDescending(conversion => conversion.IsBaseUnit)
            .ThenByDescending(conversion => conversion.IsDefaultForSale)
            .ThenBy(conversion => conversion.SortOrder)
            .ThenBy(conversion => conversion.Id)
            .Select(conversion =>
            {
                var sellPrice = conversion.Price ?? variant.Price ?? product.BasePrice;

                return new InventoryInquiryUnitDto
                {
                    UnitName = conversion.Unit?.Name ?? string.Empty,
                    Factor = conversion.Factor,
                    Barcode = ResolveUnitBarcode(conversion),
                    SellPrice = sellPrice,
                    IsBaseUnit = conversion.IsBaseUnit,
                    IsDefaultForSale = conversion.IsDefaultForSale
                };
            })
            .ToList();

        return new InventoryInquiryQuickViewDto
        {
            Cost = await InventoryCostReadAccess.CanViewAsync(_db, storeId, costViewerUserId, ct)
                ? await BuildCostQuery(BuildBaseQuery(storeId)
                    .Where(x => x.Id == balance.Id), storeId).SingleOrDefaultAsync(ct)
                : null,
            WarehouseId = balance.WarehouseId,
            ProductVariantId = balance.ProductVariantId,
            WarehouseName = balance.Warehouse.Name,
            ProductName = product.Name,
            VariantName = variant.ProductVariantName,
            ImageUrl = NormalizeImageUrl(variantImagePath ?? productImagePath),
            Barcode = ResolveRepresentativeBarcode(variant),
            BaseUnitName = product.BaseUnit?.Name ?? string.Empty,
            OnHandQty = balance.OnHandQty,
            ReservedQty = balance.ReservedQty,
            AvailableQty = balance.OnHandQty - balance.ReservedQty,
            State = ResolveState(balance.OnHandQty),
            Units = units
        };
    }

    private IQueryable<InventoryBalance> BuildBaseQuery(int storeId)
        => _db.InventoryBalances
            .AsNoTracking()
            .Where(balance =>
                balance.StoreId == storeId
                && !balance.IsDeleted
                && balance.Warehouse.StoreId == storeId
                && !balance.Warehouse.IsDeleted
                && balance.ProductVariant.StoreId == storeId
                && !balance.ProductVariant.IsDeleted
                && balance.ProductVariant.Product.StoreId == storeId
                && !balance.ProductVariant.Product.IsDeleted);

    private IQueryable<InventoryInquiryCostDto> BuildCostQuery(
        IQueryable<InventoryBalance> balances, int storeId)
        => balances.Select(balance => new InventoryInquiryCostDto
        {
            WarehouseId = balance.WarehouseId,
            ProductVariantId = balance.ProductVariantId,
            BaseUnitName = balance.ProductVariant.Product.BaseUnit.Name,
            NextFifoUnitCost = _db.InventoryCostLayers
                .Where(layer => layer.StoreId == storeId && !layer.IsDeleted
                    && layer.WarehouseId == balance.WarehouseId
                    && layer.ProductVariantId == balance.ProductVariantId && layer.RemainingQuantity > 0)
                .OrderBy(layer => layer.OccurredAtUtc).ThenBy(layer => layer.Id)
                .Select(layer => (decimal?)layer.UnitCost).FirstOrDefault(),
            NextFifoRemainingQuantity = _db.InventoryCostLayers
                .Where(layer => layer.StoreId == storeId && !layer.IsDeleted
                    && layer.WarehouseId == balance.WarehouseId
                    && layer.ProductVariantId == balance.ProductVariantId && layer.RemainingQuantity > 0)
                .OrderBy(layer => layer.OccurredAtUtc).ThenBy(layer => layer.Id)
                .Select(layer => (decimal?)layer.RemainingQuantity).FirstOrDefault(),
            NextFifoIsProvisional = _db.InventoryCostLayers
                .Where(layer => layer.StoreId == storeId && !layer.IsDeleted
                    && layer.WarehouseId == balance.WarehouseId
                    && layer.ProductVariantId == balance.ProductVariantId && layer.RemainingQuantity > 0)
                .OrderBy(layer => layer.OccurredAtUtc).ThenBy(layer => layer.Id)
                .Select(layer => layer.IsProvisionalSource).FirstOrDefault(),
            AverageUnitCost = balance.OnHandQty > 0 && balance.LastValuationAtUtc != null
                ? balance.AverageUnitCost : null,
            InventoryValue = balance.LastValuationAtUtc != null ? balance.InventoryValue : null,
            LastInboundUnitCost = balance.LastInboundUnitCost,
            LastInboundAtUtc = balance.LastInboundAtUtc,
            HasProvisionalCost = balance.OnHandQty < 0 || _db.InventoryCostLayerAllocations
                .Any(allocation => allocation.StoreId == storeId && !allocation.IsDeleted
                    && allocation.InventoryValuationEntry.StoreId == storeId
                    && !allocation.InventoryValuationEntry.IsDeleted
                    && allocation.InventoryValuationEntry.WarehouseId == balance.WarehouseId
                    && allocation.InventoryValuationEntry.ProductVariantId == balance.ProductVariantId
                    && allocation.IsProvisional && !allocation.IsResolved
                    && allocation.Quantity > allocation.ResolvedQuantity)
        });

    private IQueryable<InventoryBalance> ApplyKeyword(
        IQueryable<InventoryBalance> query,
        int storeId,
        string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return query;

        var search = keyword.Trim();

        if (_db.Database.IsRelational())
        {
            var accentInsensitiveSearch = search
                .Replace('Đ', 'D')
                .Replace('đ', 'd');

            return query.Where(balance =>
                EF.Functions.Collate(
                    balance.ProductVariant.Product.Name
                        .Replace("Đ", "D")
                        .Replace("đ", "d"),
                    AccentInsensitiveSearchCollation)
                    .Contains(accentInsensitiveSearch)
                || balance.ProductVariant.Sku.Contains(search)
                || balance.ProductVariant.UnitConversions.Any(conversion =>
                    conversion.StoreId == storeId
                    && !conversion.IsDeleted
                    && conversion.IsActive
                    && conversion.Barcodes.Any(barcode =>
                        barcode.StoreId == storeId
                        && !barcode.IsDeleted
                        && barcode.IsActive
                        && barcode.Barcode.Contains(search))));
        }

        return query.Where(balance =>
            balance.ProductVariant.Product.Name.Contains(search)
            || balance.ProductVariant.Sku.Contains(search)
            || balance.ProductVariant.UnitConversions.Any(conversion =>
                conversion.StoreId == storeId
                && !conversion.IsDeleted
                && conversion.IsActive
                && conversion.Barcodes.Any(barcode =>
                    barcode.StoreId == storeId
                    && !barcode.IsDeleted
                    && barcode.IsActive
                    && barcode.Barcode.Contains(search))));
    }

    private static IQueryable<InventoryBalance> ApplyState(
        IQueryable<InventoryBalance> query,
        string? state)
        => state switch
        {
            InventoryInquiryStates.Negative => query.Where(x => x.OnHandQty < 0),
            InventoryInquiryStates.Zero => query.Where(x => x.OnHandQty == 0),
            InventoryInquiryStates.Positive => query.Where(x => x.OnHandQty > 0),
            _ => query
        };

    private static IQueryable<InventoryBalance> ApplySort(
        IQueryable<InventoryBalance> query,
        string? sortBy,
        string? sortDirection)
    {
        var descending = string.Equals(
            sortDirection,
            "desc",
            StringComparison.OrdinalIgnoreCase);

        return sortBy switch
        {
            "warehouse" => descending
                ? query.OrderByDescending(x => x.Warehouse.Name)
                    .ThenByDescending(x => x.ProductVariant.Product.Name)
                : query.OrderBy(x => x.Warehouse.Name)
                    .ThenBy(x => x.ProductVariant.Product.Name),
            "onhandqty" => descending
                ? query.OrderByDescending(x => x.OnHandQty)
                    .ThenByDescending(x => x.ProductVariantId)
                : query.OrderBy(x => x.OnHandQty)
                    .ThenBy(x => x.ProductVariantId),
            "reservedqty" => descending
                ? query.OrderByDescending(x => x.ReservedQty)
                    .ThenByDescending(x => x.ProductVariantId)
                : query.OrderBy(x => x.ReservedQty)
                    .ThenBy(x => x.ProductVariantId),
            "availableqty" => descending
                ? query.OrderByDescending(x => x.OnHandQty - x.ReservedQty)
                    .ThenByDescending(x => x.ProductVariantId)
                : query.OrderBy(x => x.OnHandQty - x.ReservedQty)
                    .ThenBy(x => x.ProductVariantId),
            _ => descending
                ? query.OrderByDescending(x => x.ProductVariant.Product.Name)
                    .ThenByDescending(x => x.ProductVariantId)
                : query.OrderBy(x => x.ProductVariant.Product.Name)
                    .ThenBy(x => x.ProductVariantId)
        };
    }

    private static string ResolveState(decimal onHandQty)
        => onHandQty < 0
            ? InventoryInquiryStates.Negative
            : onHandQty > 0
                ? InventoryInquiryStates.Positive
                : InventoryInquiryStates.Zero;

    private static string? ResolveRepresentativeBarcode(ProductVariant variant)
        => variant.UnitConversions
            .Where(conversion => !conversion.IsDeleted && conversion.IsActive)
            .SelectMany(conversion => conversion.Barcodes
                .Where(barcode => !barcode.IsDeleted && barcode.IsActive)
                .Select(barcode => new
                {
                    Conversion = conversion,
                    Barcode = barcode
                }))
            .OrderByDescending(item => item.Conversion.IsDefaultForSale)
            .ThenByDescending(item => item.Conversion.IsBaseUnit)
            .ThenByDescending(item => item.Barcode.IsPrimary)
            .ThenBy(item => item.Barcode.Id)
            .Select(item => item.Barcode.Barcode)
            .FirstOrDefault();

    private static string? ResolveUnitBarcode(ProductUnitConversion conversion)
        => conversion.Barcodes
            .Where(barcode => !barcode.IsDeleted && barcode.IsActive)
            .OrderByDescending(barcode => barcode.IsPrimary)
            .ThenBy(barcode => barcode.Id)
            .Select(barcode => barcode.Barcode)
            .FirstOrDefault();

    private static string? NormalizeImageUrl(string? storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
            return null;

        var normalized = storagePath.Trim();

        if (normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        return "/" + normalized.TrimStart('/');
    }
}
