using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Inventory;

public sealed class InventoryLedgerIndexReadRepository : IInventoryLedgerIndexReadRepository
{
    private const string AccentInsensitiveSearchCollation =
        "Latin1_General_100_CI_AI";

    private readonly AppDbContext _db;

    public InventoryLedgerIndexReadRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<InventoryLedgerIndexPageDto> QueryAsync(
        int storeId,
        InventoryLedgerIndexQueryRequest request,
        int? costViewerUserId,
        CancellationToken ct = default)
    {
        var query = ApplyBaseFilters(
            BuildBaseQuery(storeId),
            storeId,
            request);
        var aggregateQuery = string.IsNullOrWhiteSpace(request.Keyword)
            ? ApplyBaseFilters(
                BuildAggregateQuery(storeId),
                storeId,
                request)
            : query;

        var summary = request.IncludeSummary
            ? await aggregateQuery
                .GroupBy(_ => 1)
                .Select(group => new InventoryLedgerIndexSummaryDto
                {
                    TotalItems = group.Count(),
                    IncreaseItems = group.Count(x => x.QuantityChange > 0),
                    DecreaseItems = group.Count(x => x.QuantityChange < 0),
                    NegativeItems = group.Count(x => x.AfterQty < 0)
                })
                .SingleOrDefaultAsync(ct)
                ?? new InventoryLedgerIndexSummaryDto()
            : new InventoryLedgerIndexSummaryDto();

        query = ApplyState(query, request.State);
        query = ApplySort(query, request.SortBy, request.SortDirection);

        var totalItems = request.IncludeSummary
            ? GetStateCount(summary, request.State)
            : await ApplyState(aggregateQuery, request.State).CountAsync(ct);
        var totalPages = Math.Max(
            1,
            (int)Math.Ceiling((double)totalItems / request.PageSize));
        var page = Math.Min(request.Page, totalPages);

        var items = await ProjectRows(query, storeId)
            .Skip((page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        NormalizeImageUrls(items);
        var canViewCost = await InventoryCostReadAccess.CanViewAsync(_db, storeId, costViewerUserId, ct);
        if (canViewCost)
            await PopulateInboundCostsAsync(storeId, items, ct);

        return new InventoryLedgerIndexPageDto
        {
            CanViewCost = canViewCost,
            Page = page,
            PageSize = request.PageSize,
            TotalItems = totalItems,
            SummaryIncluded = request.IncludeSummary,
            Summary = summary,
            Items = items
        };
    }

    public async Task<InventoryLedgerIndexQuickViewDto?> GetQuickViewAsync(
        int storeId,
        int transactionId,
        int? costViewerUserId,
        CancellationToken ct = default)
    {
        var item = await ProjectRows(
                BuildBaseQuery(storeId)
                    .Where(transaction => transaction.Id == transactionId),
                storeId)
            .FirstOrDefaultAsync(ct);

        if (item is null)
            return null;

        item.ImageUrl = NormalizeImageUrl(item.ImageUrl);
        if (await InventoryCostReadAccess.CanViewAsync(_db, storeId, costViewerUserId, ct))
            await PopulateInboundCostsAsync(storeId, [item], ct);

        return new InventoryLedgerIndexQuickViewDto
        {
            Item = item
        };
    }

    private IQueryable<InventoryTransaction> BuildBaseQuery(int storeId)
        => _db.InventoryTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.StoreId == storeId
                && !transaction.IsDeleted
                && transaction.Warehouse.StoreId == storeId
                && !transaction.Warehouse.IsDeleted
                && transaction.ProductVariant.StoreId == storeId
                && !transaction.ProductVariant.IsDeleted
                && transaction.ProductVariant.Product.StoreId == storeId
                && !transaction.ProductVariant.Product.IsDeleted);

    private IQueryable<InventoryTransaction> BuildAggregateQuery(int storeId)
        => _db.InventoryTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(transaction =>
                transaction.StoreId == storeId
                && !transaction.IsDeleted);

    private async Task PopulateInboundCostsAsync(
        int storeId, IReadOnlyCollection<InventoryLedgerIndexItemDto> items, CancellationToken ct)
    {
        var transactionIds = items.Where(item => item.QuantityChange > 0)
            .Select(item => item.TransactionId).ToArray();
        if (transactionIds.Length == 0) return;

        // Read the original inbound valuation, including exhausted FIFO lots.
        // Exclude outbound/revaluation entries: replenishing negative stock can
        // produce revaluations in the same transaction that are not receipt cost.
        var entries = await _db.InventoryValuationEntries.AsNoTracking()
            .Where(entry => entry.StoreId == storeId && !entry.IsDeleted
                && transactionIds.Contains(entry.InventoryTransactionId)
                && entry.InventoryTransaction.StoreId == storeId
                && !entry.InventoryTransaction.IsDeleted
                && entry.WarehouseId == entry.InventoryTransaction.WarehouseId
                && entry.ProductVariantId == entry.InventoryTransaction.ProductVariantId
                && entry.EntryType == InventoryValuationEntryType.Inbound && entry.Quantity > 0)
            .Select(entry => new
            {
                entry.InventoryTransactionId,
                entry.UnitCost,
                entry.Quantity,
                entry.Amount,
                entry.IsProvisional,
                BaseUnitName = entry.InventoryTransaction.ProductVariant.Product.BaseUnit.Name
            }).ToListAsync(ct);
        var costs = entries.GroupBy(entry => entry.InventoryTransactionId)
            .ToDictionary(group => group.Key, group => new InventoryLedgerInboundCostDto
            {
                UnitCost = group.Sum(entry => entry.UnitCost * entry.Quantity) / group.Sum(entry => entry.Quantity),
                TotalCost = group.Sum(entry => entry.Amount),
                BaseUnitName = group.First().BaseUnitName,
                IsProvisional = group.Any(entry => entry.IsProvisional),
                IsMixedCost = group.Select(entry => entry.UnitCost).Distinct().Skip(1).Any()
            });
        foreach (var item in items)
            item.InboundCost = costs.GetValueOrDefault(item.TransactionId);
    }

    private IQueryable<InventoryTransaction> ApplyBaseFilters(
        IQueryable<InventoryTransaction> query,
        int storeId,
        InventoryLedgerIndexQueryRequest request)
    {
        if (request.WarehouseId.HasValue)
            query = query.Where(x => x.WarehouseId == request.WarehouseId.Value);

        if (request.TransactionType.HasValue)
            query = query.Where(x => x.TransactionType == request.TransactionType.Value);

        if (request.ReferenceType.HasValue)
            query = query.Where(x => x.ReferenceType == request.ReferenceType.Value);

        if (request.FromDate.HasValue)
        {
            var fromDate = request.FromDate.Value.Date;
            query = query.Where(x => x.OccurredAtUtc >= fromDate);
        }

        if (request.ToDate.HasValue)
        {
            var toDateExclusive = request.ToDate.Value.Date.AddDays(1);
            query = query.Where(x => x.OccurredAtUtc < toDateExclusive);
        }

        if (!string.IsNullOrWhiteSpace(request.ReferenceCode))
        {
            var referenceCode = request.ReferenceCode.Trim();
            query = query.Where(x =>
                x.ReferenceId != null
                && x.ReferenceId.Contains(referenceCode));
        }

        return ApplyKeyword(query, storeId, request.Keyword);
    }

    private IQueryable<InventoryTransaction> ApplyKeyword(
        IQueryable<InventoryTransaction> query,
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

            return query.Where(transaction =>
                EF.Functions.Collate(
                    transaction.ProductVariant.Product.Name
                        .Replace("Đ", "D")
                        .Replace("đ", "d"),
                    AccentInsensitiveSearchCollation)
                    .Contains(accentInsensitiveSearch)
                || transaction.ProductVariant.Sku.Contains(search)
                || transaction.ProductVariant.UnitConversions.Any(conversion =>
                    conversion.StoreId == storeId
                    && !conversion.IsDeleted
                    && conversion.IsActive
                    && conversion.Barcodes.Any(barcode =>
                        barcode.StoreId == storeId
                        && !barcode.IsDeleted
                        && barcode.IsActive
                        && barcode.Barcode.Contains(search)))
                || (transaction.ReferenceId != null
                    && transaction.ReferenceId.Contains(search))
                || (transaction.Note != null
                    && EF.Functions.Collate(
                        transaction.Note
                            .Replace("Đ", "D")
                            .Replace("đ", "d"),
                        AccentInsensitiveSearchCollation)
                        .Contains(accentInsensitiveSearch)));
        }

        return query.Where(transaction =>
            transaction.ProductVariant.Product.Name.Contains(search)
            || transaction.ProductVariant.Sku.Contains(search)
            || transaction.ProductVariant.UnitConversions.Any(conversion =>
                conversion.StoreId == storeId
                && !conversion.IsDeleted
                && conversion.IsActive
                && conversion.Barcodes.Any(barcode =>
                    barcode.StoreId == storeId
                    && !barcode.IsDeleted
                    && barcode.IsActive
                    && barcode.Barcode.Contains(search)))
            || (transaction.ReferenceId != null
                && transaction.ReferenceId.Contains(search))
            || (transaction.Note != null
                && transaction.Note.Contains(search)));
    }

    private static IQueryable<InventoryTransaction> ApplyState(
        IQueryable<InventoryTransaction> query,
        string? state)
        => state switch
        {
            InventoryLedgerIndexStates.Increase =>
                query.Where(x => x.QuantityChange > 0),
            InventoryLedgerIndexStates.Decrease =>
                query.Where(x => x.QuantityChange < 0),
            InventoryLedgerIndexStates.Negative =>
                query.Where(x => x.AfterQty < 0),
            _ => query
        };

    private static int GetStateCount(
        InventoryLedgerIndexSummaryDto summary,
        string? state)
        => state switch
        {
            InventoryLedgerIndexStates.Increase => summary.IncreaseItems,
            InventoryLedgerIndexStates.Decrease => summary.DecreaseItems,
            InventoryLedgerIndexStates.Negative => summary.NegativeItems,
            _ => summary.TotalItems
        };

    private static IQueryable<InventoryTransaction> ApplySort(
        IQueryable<InventoryTransaction> query,
        string? sortBy,
        string? sortDirection)
    {
        var descending = !string.Equals(
            sortDirection,
            "asc",
            StringComparison.OrdinalIgnoreCase);

        return sortBy switch
        {
            "product" => descending
                ? query.OrderByDescending(x => x.ProductVariant.Product.Name)
                    .ThenByDescending(x => x.OccurredAtUtc)
                    .ThenByDescending(x => x.Id)
                : query.OrderBy(x => x.ProductVariant.Product.Name)
                    .ThenBy(x => x.OccurredAtUtc)
                    .ThenBy(x => x.Id),
            "warehouse" => descending
                ? query.OrderByDescending(x => x.Warehouse.Name)
                    .ThenByDescending(x => x.OccurredAtUtc)
                    .ThenByDescending(x => x.Id)
                : query.OrderBy(x => x.Warehouse.Name)
                    .ThenBy(x => x.OccurredAtUtc)
                    .ThenBy(x => x.Id),
            "change" => descending
                ? query.OrderByDescending(x => x.QuantityChange)
                    .ThenByDescending(x => x.OccurredAtUtc)
                    .ThenByDescending(x => x.Id)
                : query.OrderBy(x => x.QuantityChange)
                    .ThenBy(x => x.OccurredAtUtc)
                    .ThenBy(x => x.Id),
            "after" => descending
                ? query.OrderByDescending(x => x.AfterQty)
                    .ThenByDescending(x => x.OccurredAtUtc)
                    .ThenByDescending(x => x.Id)
                : query.OrderBy(x => x.AfterQty)
                    .ThenBy(x => x.OccurredAtUtc)
                    .ThenBy(x => x.Id),
            _ => descending
                ? query.OrderByDescending(x => x.OccurredAtUtc)
                    .ThenByDescending(x => x.Id)
                : query.OrderBy(x => x.OccurredAtUtc)
                    .ThenBy(x => x.Id)
        };
    }

    private static IQueryable<InventoryLedgerIndexItemDto> ProjectRows(
        IQueryable<InventoryTransaction> query,
        int storeId)
        => query.Select(transaction => new InventoryLedgerIndexItemDto
        {
            TransactionId = transaction.Id,
            WarehouseName = transaction.Warehouse.Name,
            ProductName = transaction.ProductVariant.Product.Name,
            VariantName = transaction.ProductVariant.ProductVariantName,
            Sku = transaction.ProductVariant.Sku,
            Barcode = transaction.ProductVariant.UnitConversions
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
            ImageUrl = transaction.ProductVariant.PrimaryProductImage != null
                && !transaction.ProductVariant.PrimaryProductImage.IsDeleted
                    ? transaction.ProductVariant.PrimaryProductImage.MediaAsset.StoragePath
                    : transaction.ProductVariant.Product.ProductImages
                        .Where(image => !image.IsDeleted)
                        .OrderByDescending(image => image.IsPrimary)
                        .ThenBy(image => image.SortOrder)
                        .ThenBy(image => image.Id)
                        .Select(image => image.MediaAsset.StoragePath)
                        .FirstOrDefault(),
            TransactionType = transaction.TransactionType,
            ReferenceType = transaction.ReferenceType,
            ReferenceCode = transaction.ReferenceId,
            BeforeQty = transaction.BeforeQty,
            QuantityChange = transaction.QuantityChange,
            AfterQty = transaction.AfterQty,
            IsNegativeAfterTransaction = transaction.AfterQty < 0,
            OccurredAtUtc = transaction.OccurredAtUtc,
            Note = transaction.Note
        });

    private static void NormalizeImageUrls(
        IEnumerable<InventoryLedgerIndexItemDto> items)
    {
        foreach (var item in items)
            item.ImageUrl = NormalizeImageUrl(item.ImageUrl);
    }

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
