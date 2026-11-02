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
    // Bound request memory. This selects a query plan; it never limits search results.
    private const int ResolvedTextMatchLimit = 4096;

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
        var referenceReceiptCodes = await ResolveReceiptReferenceCodesAsync(storeId, request.ReferenceCode, ct);
        var directReferenceCodes = await ResolveDirectReferenceCodesAsync(storeId, request.ReferenceCode, ct);
        var ledgerCandidates = ApplyBaseFilters(BuildAggregateQuery(storeId), storeId,
            request, referenceReceiptCodes, directReferenceCodes);
        var query = ApplyBaseFilters(
            BuildBaseQuery(storeId),
            storeId,
            request, referenceReceiptCodes, directReferenceCodes);
        query = await ApplyKeywordAsync(query, ledgerCandidates, storeId, request.Keyword, request.SearchScope, ct);
        var aggregateQuery = string.IsNullOrWhiteSpace(request.Keyword)
            ? ledgerCandidates
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

        // Page narrow IDs before loading images/barcodes. Correlated display subqueries must
        // not change the search plan into a timeline scan when the result set is sparse.
        var pageIds = await query.Select(t => t.Id)
            .Skip((page - 1) * request.PageSize).Take(request.PageSize).ToArrayAsync(ct);
        var canViewCost = await InventoryCostReadAccess.CanViewAsync(_db, storeId, costViewerUserId, ct);
        var items = pageIds.Length == 0 ? new List<InventoryLedgerIndexItemDto>()
            : await ProjectRows(BuildBaseQuery(storeId).Where(t => pageIds.Contains(t.Id)), storeId, canViewCost).ToListAsync(ct);
        var positions = pageIds.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
        items.Sort((left, right) => positions[left.TransactionId].CompareTo(positions[right.TransactionId]));

        NormalizeImageUrls(items);
        await PopulateReceiptReferencesAsync(storeId, items, ct);
        await PopulateSourcesAsync(storeId, items, ct);
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
        var canViewCost = await InventoryCostReadAccess.CanViewAsync(_db, storeId, costViewerUserId, ct);
        var item = await ProjectRows(
                BuildBaseQuery(storeId)
                    .Where(transaction => transaction.Id == transactionId),
                storeId, canViewCost)
            .FirstOrDefaultAsync(ct);

        if (item is null)
            return null;

        item.ImageUrl = NormalizeImageUrl(item.ImageUrl);
        await PopulateReceiptReferencesAsync(storeId, [item], ct);
        await PopulateSourcesAsync(storeId, [item], ct);
        if (canViewCost)
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

    public async Task<InventoryLedgerPeriodDto?> GetPeriodAsync(int storeId, int warehouseId,
        int productVariantId, DateTime? fromDate, DateTime? toDate, CancellationToken ct = default)
    {
        var result = await (from warehouse in _db.Warehouses.AsNoTracking()
            where warehouse.StoreId == storeId && !warehouse.IsDeleted && warehouse.Id == warehouseId
            from variant in _db.ProductVariants.AsNoTracking()
            where variant.StoreId == storeId && !variant.IsDeleted && variant.Id == productVariantId
                && variant.Product.StoreId == storeId && !variant.Product.IsDeleted
            select new InventoryLedgerPeriodDto
            {
                WarehouseId = warehouse.Id, WarehouseName = warehouse.Name,
                ProductVariantId = variant.Id, ProductName = variant.Product.Name,
                VariantName = variant.ProductVariantName, BaseUnitName = variant.Product.BaseUnit.Name,
                FromDate = fromDate, ToDate = toDate
            }).SingleOrDefaultAsync(ct);
        if (result is null) return null;

        var start = fromDate.HasValue ? VietnamTime.ToUtc(fromDate.Value.Date) : (DateTime?)null;
        var end = toDate.HasValue ? VietnamTime.ToUtc(toDate.Value.Date.AddDays(1)) : (DateTime?)null;
        // BeforeQty/AfterQty are posting-time snapshots, not a business-date running balance.
        // Start from the first durable posting's baseline, then include ALL movements by
        // OccurredAtUtc. This also handles late/backdated postings and empty periods.
        var totals = await BuildBaseQuery(storeId)
            .Where(t => t.WarehouseId == warehouseId && t.ProductVariantId == productVariantId)
            .GroupBy(t => new { t.WarehouseId, t.ProductVariantId })
            .Select(g => new
            {
                Baseline = g.OrderBy(t => t.Id).Select(t => t.BeforeQty).First(),
                Prior = g.Sum(t => start.HasValue && t.OccurredAtUtc < start.Value ? t.QuantityChange : 0m),
                Increase = g.Sum(t => (!start.HasValue || t.OccurredAtUtc >= start.Value)
                    && (!end.HasValue || t.OccurredAtUtc < end.Value) && t.QuantityChange > 0 ? t.QuantityChange : 0m),
                Decrease = g.Sum(t => (!start.HasValue || t.OccurredAtUtc >= start.Value)
                    && (!end.HasValue || t.OccurredAtUtc < end.Value) && t.QuantityChange < 0 ? -t.QuantityChange : 0m)
            }).SingleOrDefaultAsync(ct);
        if (totals is null) return result; // No ledger: do not invent a historical balance from today's stock.
        result.HasRecordedHistory = true;
        result.OpeningQty = totals.Baseline + totals.Prior;
        result.IncreaseQty = totals.Increase;
        result.DecreaseQty = totals.Decrease;
        result.ClosingQty = result.OpeningQty + totals.Increase - totals.Decrease;
        return result;
    }

    private async Task PopulateSourcesAsync(int storeId, IReadOnlyCollection<InventoryLedgerIndexItemDto> items,
        CancellationToken ct)
    {
        // ReferenceId is a typed durable identity, never a display code or arbitrary URL.
        int[] Ids(InventoryReferenceType type) => items.Where(i => i.ReferenceType == type)
            .Select(i => int.TryParse(i.ReferenceCode, out var id) && id > 0
                && id.ToString(System.Globalization.CultureInfo.InvariantCulture) == i.ReferenceCode ? id : 0)
            .Where(id => id > 0).Distinct().ToArray();
        void Attach(InventoryReferenceType type, string kind, IEnumerable<(int ReferenceId, int TargetId)> sources)
        {
            var map = sources.ToDictionary(x => x.ReferenceId, x => x.TargetId);
            foreach (var item in items.Where(i => i.ReferenceType == type))
                if (int.TryParse(item.ReferenceCode, out var id) && map.TryGetValue(id, out var target))
                { item.SourceKind = kind; item.SourceId = target; }
        }

        var ids = Ids(InventoryReferenceType.Order);
        if (ids.Length > 0) Attach(InventoryReferenceType.Order, "order",
            (await _db.Orders.AsNoTracking().Where(x => x.StoreId == storeId && !x.IsDeleted && ids.Contains(x.Id))
                .Select(x => x.Id).ToArrayAsync(ct)).Select(id => (id, id)));
        ids = Ids(InventoryReferenceType.Refund);
        if (ids.Length > 0) Attach(InventoryReferenceType.Refund, "order",
            (await _db.SalesReturns.AsNoTracking().Where(x => x.StoreId == storeId && !x.IsDeleted
                && ids.Contains(x.Id) && x.Order.StoreId == storeId && !x.Order.IsDeleted)
                .Select(x => new { x.Id, x.OrderId }).ToArrayAsync(ct)).Select(x => (x.Id, x.OrderId)));
        ids = Ids(InventoryReferenceType.StockDocument);
        if (ids.Length > 0)
        {
            var documents = await _db.StockDocuments.AsNoTracking().Where(x => x.StoreId == storeId && !x.IsDeleted
                && x.Type == StockDocumentType.Receipt && ids.Contains(x.Id))
                .Select(x => new { x.Id, x.ReceiptSource }).ToArrayAsync(ct);
            Attach(InventoryReferenceType.StockDocument, "receipt", documents
                .Where(x => x.ReceiptSource != PurchaseReceiptSource.PurchaseOrder).Select(x => (x.Id, x.Id)));
            Attach(InventoryReferenceType.StockDocument, "purchase-receipt", documents
                .Where(x => x.ReceiptSource == PurchaseReceiptSource.PurchaseOrder).Select(x => (x.Id, x.Id)));
        }
        ids = Ids(InventoryReferenceType.StockCount);
        if (ids.Length > 0) Attach(InventoryReferenceType.StockCount, "count",
            (await _db.StockCountDocuments.AsNoTracking().Where(x => x.StoreId == storeId && !x.IsDeleted && ids.Contains(x.Id))
                .Select(x => x.Id).ToArrayAsync(ct)).Select(id => (id, id)));
        ids = Ids(InventoryReferenceType.StockTransfer);
        if (ids.Length > 0) Attach(InventoryReferenceType.StockTransfer, "transfer",
            (await _db.StockTransferDocuments.AsNoTracking().Where(x => x.StoreId == storeId && !x.IsDeleted && ids.Contains(x.Id))
                .Select(x => x.Id).ToArrayAsync(ct)).Select(id => (id, id)));
        ids = Ids(InventoryReferenceType.Adjustment);
        if (ids.Length > 0) Attach(InventoryReferenceType.Adjustment, "adjustment",
            (await _db.InventoryAdjustmentDocuments.AsNoTracking().Where(x => x.StoreId == storeId && !x.IsDeleted && ids.Contains(x.Id))
                .Select(x => x.Id).ToArrayAsync(ct)).Select(id => (id, id)));
        ids = Ids(InventoryReferenceType.Delivery);
        if (ids.Length > 0) Attach(InventoryReferenceType.Delivery, "delivery",
            (await _db.DeliveryOrders.AsNoTracking().Where(x => x.StoreId == storeId && !x.IsDeleted && ids.Contains(x.Id))
                .Select(x => x.Id).ToArrayAsync(ct)).Select(id => (id, id)));
    }

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
        InventoryLedgerIndexQueryRequest request, string[] referenceReceiptCodes, string[]? directReferenceCodes)
    {
        if (request.WarehouseId.HasValue)
            query = query.Where(x => x.WarehouseId == request.WarehouseId.Value);

        if (request.TransactionType.HasValue)
            query = query.Where(x => x.TransactionType == request.TransactionType.Value);

        if (request.ReferenceType.HasValue)
            query = query.Where(x => x.ReferenceType == request.ReferenceType.Value);

        if (request.FromDate.HasValue)
        {
            var fromDate = VietnamTime.ToUtc(request.FromDate.Value.Date);
            query = query.Where(x => x.OccurredAtUtc >= fromDate);
        }

        if (request.ToDate.HasValue)
        {
            var toDateExclusive = VietnamTime.ToUtc(request.ToDate.Value.Date.AddDays(1));
            query = query.Where(x => x.OccurredAtUtc < toDateExclusive);
        }

        if (!string.IsNullOrWhiteSpace(request.ReferenceCode))
        {
            var referenceCode = request.ReferenceCode.Trim();
            query = ApplyReferenceMatches(query, referenceCode, referenceReceiptCodes, directReferenceCodes);
        }

        return query;
    }

    private async Task<IQueryable<InventoryTransaction>> ApplyKeywordAsync(
        IQueryable<InventoryTransaction> query, IQueryable<InventoryTransaction> ledgerCandidates,
        int storeId, string? keyword, string? scope, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return query;
        var search = keyword.Trim();
        var normalized = search.Replace('Đ', 'D').Replace('đ', 'd');
        if (scope == "reference")
            return ApplyReferenceMatches(query, search,
                await ResolveReceiptReferenceCodesAsync(storeId, search, ct),
                await ResolveDirectReferenceCodesAsync(storeId, search, ct));
        if (scope == "note")
            return (await ResolveTextMatchesAsync(query, ledgerCandidates, search, normalized, notesOnly: true, ct)).Matches;
        var variants = _db.ProductVariants.AsNoTracking().Where(v =>
            v.StoreId == storeId && !v.IsDeleted &&
            v.Product.StoreId == storeId && !v.Product.IsDeleted);

        // Resolve the catalog once, not the same barcode/name joins for every historical movement.
        // Keep inactive products searchable: this is a historical ledger, not a POS catalog.
        var names = _db.Database.IsRelational()
            ? variants.Where(v => EF.Functions.Collate(
                v.Product.Name.Replace("Đ", "D").Replace("đ", "d"),
                AccentInsensitiveSearchCollation).Contains(normalized))
            : variants.Where(v => v.Product.Name.Contains(search));
        var codes = variants.Where(v => v.Sku.Contains(search) || v.UnitConversions.Any(c =>
            c.StoreId == storeId && !c.IsDeleted && c.IsActive && c.Barcodes.Any(b =>
                b.StoreId == storeId && !b.IsDeleted && b.IsActive && b.Barcode.Contains(search))));
        var variantIds = await names.Select(v => v.Id).Union(codes.Select(v => v.Id)).ToArrayAsync(ct);
        // Equality gives SQL Server the actual product selectivity instead of OPENJSON's
        // fixed collection estimate, especially for rare products on later pages.
        var productMatches = variantIds.Length == 1
            ? query.Where(t => t.ProductVariantId == variantIds[0])
            : variantIds.Length is > 0 and <= 128 && _db.Database.IsRelational()
                ? query.Where(t => EF.Constant(variantIds).Contains(t.ProductVariantId))
                : query.Where(t => variantIds.Contains(t.ProductVariantId));
        if (scope == "product")
            return variantIds.Length == 0 ? query.Where(t => false)
                : productMatches;

        // Resolve sparse text matches ONCE before the summary/page plans. Nesting a
        // text scan under the product UNION caused repeated scans of the store ledger.
        var textResolution = await ResolveTextMatchesAsync(query, ledgerCandidates, search, normalized, notesOnly: false, ct);
        var xmlReceiptCodes = await ResolveReceiptReferenceCodesAsync(storeId, search, ct);
        if (textResolution.IsBroad)
        {
            // A broad mixed product/text search must not fall back to the original
            // UNION-inside-semi-join plan. Filter the same scoped row once, including
            // receipt/XML identities, then retain normal server-side counts/paging.
            return _db.Database.IsRelational()
                ? query.Where(t => (variantIds.Length > 0 && variantIds.Contains(t.ProductVariantId))
                    || (xmlReceiptCodes.Length > 0 && t.ReferenceType == InventoryReferenceType.StockDocument && xmlReceiptCodes.Contains(t.ReferenceId!))
                    || (t.ReferenceId != null && t.ReferenceId.Contains(search))
                    || (t.Note != null && EF.Functions.Collate(t.Note.Replace("Đ", "D").Replace("đ", "d"), AccentInsensitiveSearchCollation).Contains(normalized)))
                : query.Where(t => (variantIds.Length > 0 && variantIds.Contains(t.ProductVariantId))
                    || (xmlReceiptCodes.Length > 0 && t.ReferenceType == InventoryReferenceType.StockDocument && xmlReceiptCodes.Contains(t.ReferenceId!))
                    || (t.ReferenceId != null && t.ReferenceId.Contains(search)) || (t.Note != null && t.Note.Contains(search)));
        }
        var textMatches = textResolution.Matches;
        if (xmlReceiptCodes.Length > 0)
        {
            var textIds = textMatches.Select(t => t.Id)
                .Union(ReceiptReferenceMatches(query, xmlReceiptCodes).Select(t => t.Id));
            textMatches = query.Where(t => textIds.Contains(t.Id));
        }
        if (variantIds.Length == 0) return textMatches;

        // Separate seekable product history from free-text notes. Union IDs only, so a movement
        // matching both branches is counted once and wide ledger rows are not sorted/deduplicated.
        var matchingIds = productMatches.Select(t => t.Id)
            .Union(textMatches.Select(t => t.Id));
        return query.Where(t => matchingIds.Contains(t.Id));
    }

    private async Task<(IQueryable<InventoryTransaction> Matches, bool IsBroad)> ResolveTextMatchesAsync(
        IQueryable<InventoryTransaction> query, IQueryable<InventoryTransaction> ledgerCandidates,
        string search, string normalized, bool notesOnly, CancellationToken ct)
    {
        IQueryable<InventoryTransaction> Filter(IQueryable<InventoryTransaction> source)
            => _db.Database.IsRelational()
                ? source.Where(t => (!notesOnly && t.ReferenceId != null && t.ReferenceId.Contains(search))
                    || (t.Note != null && EF.Functions.Collate(t.Note.Replace("Đ", "D").Replace("đ", "d"),
                        AccentInsensitiveSearchCollation).Contains(normalized)))
                : source.Where(t => (!notesOnly && t.ReferenceId != null && t.ReferenceId.Contains(search))
                    || (t.Note != null && t.Note.Contains(search)));

        // Only explicit store/date/warehouse/document scope during the expensive text
        // scan. Final results ALWAYS retain all warehouse/product tenant/deletion checks.
        var ids = await Filter(ledgerCandidates).Select(t => t.Id)
            .Take(ResolvedTextMatchLimit + 1).ToArrayAsync(ct);
        if (ids.Length > ResolvedTextMatchLimit)
            return (Filter(query), true); // Broad searches remain complete, with server-side paging.
        if (ids.Length == 0) return (query.Where(t => false), false);
        if (ids.Length == 1) return (query.Where(t => t.Id == ids[0]), false);
        var matches = _db.Database.IsRelational()
            ? query.Where(t => EF.Constant(ids).Contains(t.Id))
            : query.Where(t => ids.Contains(t.Id));
        return (matches, false);
    }

    private async Task<string[]> ResolveReceiptReferenceCodesAsync(
        int storeId, string? search, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(search)) return [];
        search = search.Trim();
        var xmlSearch = search.Replace(" ", "").Replace("·", "").Replace("/", "");
        var receiptCodes = _db.StockDocuments.AsNoTracking().Where(d =>
                d.StoreId == storeId && !d.IsDeleted && d.Type == StockDocumentType.Receipt
                && d.DocumentNo.Contains(search))
            .Select(d => d.Id.ToString());
        var xmlReceiptCodes = ActiveReceiptInvoiceLinks(storeId).Where(m =>
                xmlSearch != "" && ((m.InputInvoiceHead.InvoiceSeries ?? "") + (m.InputInvoiceHead.InvoiceNumber ?? ""))
                    .Replace(" ", "").Replace("·", "").Replace("/", "").Contains(xmlSearch))
            .Select(m => m.StockDocumentId.ToString());
        return await receiptCodes.Union(xmlReceiptCodes).ToArrayAsync(ct);
    }

    private IQueryable<StockDocumentInputInvoiceMap> ActiveReceiptInvoiceLinks(int storeId)
        => _db.StockDocumentInputInvoiceMaps.AsNoTracking().Where(m =>
            m.StoreId == storeId && !m.IsDeleted
            && m.StockDocument.StoreId == storeId && !m.StockDocument.IsDeleted
            && m.StockDocument.Type == StockDocumentType.Receipt
            && m.InputInvoiceHead.StoreId == storeId && !m.InputInvoiceHead.IsDeleted);

    private async Task<string[]?> ResolveDirectReferenceCodesAsync(
        int storeId, string? search, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(search)) return [];
        search = search.Trim();
        // Resolve a sparse reference once, then seek its history for counts and
        // paging. Broad partial searches retain the existing database predicate.
        var codes = await BuildAggregateQuery(storeId)
            .Where(t => t.ReferenceId != null && t.ReferenceId.Contains(search))
            .Select(t => t.ReferenceId!).Distinct().Take(129).ToArrayAsync(ct);
        return codes.Length <= 128 ? codes : null;
    }

    private IQueryable<InventoryTransaction> ReceiptReferenceMatches(
        IQueryable<InventoryTransaction> query, string[] receiptCodes)
    {
        var receipts = query.Where(t => t.ReferenceType == InventoryReferenceType.StockDocument);
        return receiptCodes.Length == 1
            ? receipts.Where(t => t.ReferenceId == receiptCodes[0])
            : _db.Database.IsRelational()
                ? receipts.Where(t => EF.Constant(receiptCodes).Contains(t.ReferenceId!))
                : receipts.Where(t => receiptCodes.Contains(t.ReferenceId!));
    }

    private IQueryable<InventoryTransaction> ApplyReferenceMatches(
        IQueryable<InventoryTransaction> query, string search, string[] receiptCodes, string[]? directCodes)
    {
        var direct = directCodes is null
            ? query.Where(t => t.ReferenceId != null && t.ReferenceId.Contains(search))
            : directCodes.Length == 0
                ? query.Where(t => false)
            : directCodes.Length == 1
                ? query.Where(t => t.ReferenceId == directCodes[0])
                : _db.Database.IsRelational()
                    ? query.Where(t => EF.Constant(directCodes).Contains(t.ReferenceId!))
                    : query.Where(t => directCodes.Contains(t.ReferenceId!));
        if (receiptCodes.Length == 0) return direct;
        var receipts = ReceiptReferenceMatches(query, receiptCodes);
        if (directCodes is { Length: 0 }) return receipts;
        var ids = direct.Select(t => t.Id).Union(receipts.Select(t => t.Id));
        return query.Where(t => ids.Contains(t.Id));
    }

    private async Task PopulateReceiptReferencesAsync(
        int storeId, IReadOnlyCollection<InventoryLedgerIndexItemDto> items, CancellationToken ct)
    {
        var receiptItems = items.Where(item => item.ReferenceType == InventoryReferenceType.StockDocument)
            .ToList();
        var codes = receiptItems.Select(item => item.ReferenceCode).Where(code => code != null).Distinct().ToArray();
        if (codes.Length == 0) return;
        // Load only the receipts on this page; never join XML details into ledger rows.
        var receipts = await _db.StockDocuments.AsNoTracking().Where(d =>
                d.StoreId == storeId && !d.IsDeleted && d.Type == StockDocumentType.Receipt
                && codes.Contains(d.Id.ToString()))
            .Select(d => new { d.Id, d.DocumentNo }).ToListAsync(ct);
        if (receipts.Count == 0) return;
        var ids = receipts.Select(d => d.Id).ToArray();
        var invoices = await ActiveReceiptInvoiceLinks(storeId)
            .Where(m => ids.Contains(m.StockDocumentId)).OrderBy(m => m.Id)
            .Select(m => new
            {
                m.StockDocumentId, m.InputInvoiceHead.InvoiceSeries, m.InputInvoiceHead.InvoiceNumber
            }).ToListAsync(ct);
        var receiptByCode = receipts.ToDictionary(d => d.Id.ToString());
        var invoiceByReceipt = invoices.GroupBy(m => m.StockDocumentId).ToDictionary(g => g.Key,
            g => g.Select(m => new InventoryLedgerInputInvoiceDto
                { InvoiceSeries = m.InvoiceSeries, InvoiceNumber = m.InvoiceNumber }).ToList());
        foreach (var item in receiptItems)
        {
            if (item.ReferenceCode is null || !receiptByCode.TryGetValue(item.ReferenceCode, out var receipt)) continue;
            item.ReceiptDocumentNo = receipt.DocumentNo;
            item.LinkedInputInvoices = invoiceByReceipt.GetValueOrDefault(receipt.Id) ?? [];
        }
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
        int storeId, bool canViewCost)
        => query.Select(transaction => new InventoryLedgerIndexItemDto
        {
            TransactionId = transaction.Id,
            WarehouseId = transaction.WarehouseId,
            ProductVariantId = transaction.ProductVariantId,
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
            UnitCostSnapshot = canViewCost && transaction.TransactionType == InventoryTransactionType.Revaluation ? transaction.UnitCostSnapshot : (decimal?)null,
            TotalCost = canViewCost && transaction.TransactionType == InventoryTransactionType.Revaluation ? transaction.TotalCost : (decimal?)null,
            BeforeInventoryValue = canViewCost && transaction.TransactionType == InventoryTransactionType.Revaluation ? transaction.BeforeInventoryValue : (decimal?)null,
            AfterInventoryValue = canViewCost && transaction.TransactionType == InventoryTransactionType.Revaluation ? transaction.AfterInventoryValue : (decimal?)null,
            RunningAverageUnitCostAfter = canViewCost && transaction.TransactionType == InventoryTransactionType.Revaluation ? transaction.RunningAverageUnitCostAfter : (decimal?)null,
            BaseUnitName = transaction.ProductVariant.Product.BaseUnit.Name,
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
