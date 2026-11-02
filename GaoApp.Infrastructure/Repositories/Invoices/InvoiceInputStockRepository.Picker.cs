using GaoApp.Application.Common.Helpers;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Invoices;

public sealed partial class InvoiceInputStockRepository
{
    public async Task<Result<InvoiceProductPickerPageDto>> SearchProductsAsync(
        int invoiceHeadId, string keyword, int take = 20, CancellationToken ct = default)
    {
        var context = await PickerContextAsync(invoiceHeadId, ct);
        if (context.IsFailure) return Result<InvoiceProductPickerPageDto>.Failure(context.Error);
        var scope = context.Value;
        keyword = (keyword ?? "").Trim();
        if (keyword.Length > 200) return Result<InvoiceProductPickerPageDto>.Failure(Error.Validation("Từ khóa tối đa 200 ký tự."));
        if (keyword.Length < 2) return Result<InvoiceProductPickerPageDto>.Success(Page(scope, []));

        var candidates = MatchingProducts(scope.Header.StoreId, keyword);
        if (scope.RequiresStock)
        {
            // Search IDs first, in one batch, then apply documentary availability BEFORE Take.
            // No POS graph/images and no per-product stock query. Fresh reads deliberately
            // bypass the management screen's 30-second cache, including during validation.
            var ids = await candidates.Select(x => x.Id).ToArrayAsync(ct);
            if (ids.Length == 0) return Result<InvoiceProductPickerPageDto>.Success(Page(scope, []));
            var balances = await PickerBalancesAsync(scope, ids, ct);
            var eligible = balances.Where(x => x.Value.Available > 0 && x.Value.Additional > 0).Select(x => x.Key).ToArray();
            if (eligible.Length == 0) return Result<InvoiceProductPickerPageDto>.Success(Page(scope, []));
            var items = await ProjectPicker(candidates.Where(x => eligible.Contains(x.Id)), scope.Header.StoreId)
                .OrderBy(x => x.DisplayName).ThenBy(x => x.ProductVariantId).Take(Math.Clamp(take, 1, 50)).ToListAsync(ct);
            foreach (var item in items) ApplyBalance(item, scope, balances[item.ProductVariantId]);
            return Result<InvoiceProductPickerPageDto>.Success(Page(scope, items));
        }

        // Adjustment/replacement invoices do not consume documentary stock again.
        var correctionItems = await ProjectPicker(candidates, scope.Header.StoreId)
            .OrderBy(x => x.DisplayName).ThenBy(x => x.ProductVariantId).Take(Math.Clamp(take, 1, 50)).ToListAsync(ct);
        return Result<InvoiceProductPickerPageDto>.Success(Page(scope, correctionItems));
    }

    public async Task<Result<InvoiceProductPickerItemDto>> GetProductStockAsync(
        int invoiceHeadId, int productVariantId, CancellationToken ct = default)
    {
        var context = await PickerContextAsync(invoiceHeadId, ct);
        if (context.IsFailure) return Result<InvoiceProductPickerItemDto>.Failure(context.Error);
        var scope = context.Value;
        var item = await ProjectPicker(ActivePickerProducts(scope.Header.StoreId).Where(x => x.Id == productVariantId), scope.Header.StoreId).SingleOrDefaultAsync(ct);
        if (item is null) return Result<InvoiceProductPickerItemDto>.Failure(Error.NotFound("Không tìm thấy sản phẩm được chọn."));
        if (scope.RequiresStock)
        {
            var balances = await PickerBalancesAsync(scope, [productVariantId], ct);
            ApplyBalance(item, scope, balances.GetValueOrDefault(productVariantId) ?? new(0, 0, 0));
        }
        return Result<InvoiceProductPickerItemDto>.Success(item);
    }

    private async Task<Result<PickerContext>> PickerContextAsync(int invoiceHeadId, CancellationToken ct)
    {
        // New web reads require an explicit tenant; never inherit the host-job null scope.
        if (_db.CurrentStoreId is not > 0) return Result<PickerContext>.Failure(Error.NotFound("Không tìm thấy hóa đơn bán ra."));
        var header = await LoadHeaderAsync(invoiceHeadId, ct);
        if (header is null || header.StoreId != _db.CurrentStoreId) return Result<PickerContext>.Failure(Error.NotFound("Không tìm thấy hóa đơn bán ra."));
        if (header.OriginalInvoiceHeadId.HasValue || header.CorrectionType.HasValue)
            return Result<PickerContext>.Success(new(header, false, null, null));
        // This is exactly the warehouse resolution for a NEW manual line in preflight.
        var warehouseId = header.DefaultWarehouseId ?? header.OrderWarehouseId ?? header.StoreDefaultWarehouseId;
        var warehouse = await _db.Warehouses.AsNoTracking().Where(x => x.Id == warehouseId && x.StoreId == header.StoreId
                && !x.IsDeleted && x.IsActive && (!header.LegalEntityId.HasValue || x.LegalEntityId == header.LegalEntityId))
            .Select(x => new { x.Id, x.Name }).SingleOrDefaultAsync(ct);
        if (warehouse is null) return Result<PickerContext>.Failure(Error.Validation("Invoice.PickerWarehouseMissing",
            "Chưa xác định được kho hợp lệ của hóa đơn. Kiểm tra kho của pháp nhân hoặc kho mặc định cửa hàng."));
        return Result<PickerContext>.Success(new(header, true, warehouse.Id, warehouse.Name));
    }

    private async Task<Dictionary<int, PickerBalance>> PickerBalancesAsync(PickerContext scope, int[] ids, CancellationToken ct)
    {
        var movements = await new InvoiceInputStockReadRepository(_db).GetPageMovementsAsync(scope.Header.StoreId, ids, [scope.WarehouseId!.Value], ct);
        var details = await QueryCheckableDetails(scope.Header.Id).Where(x => ids.Contains(x.ProductVariantId)).ToListAsync(ct);
        var required = ResolveAndGroupDetails(details, scope.Header).Where(x => x.WarehouseId == scope.WarehouseId)
            .ToDictionary(x => x.ProductVariantId, x => x.BaseQuantity);
        // Page reads omit hydrated document links, but retain the numeric legacy reference.
        var legacyOrderId = scope.Header.LegacySourceId?.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var balances = movements.Where(x => x.WarehouseId == scope.WarehouseId && (!scope.Header.LegalEntityId.HasValue || x.LegalEntityId == scope.Header.LegalEntityId))
            .GroupBy(x => x.ProductVariantId).ToDictionary(group => group.Key, group =>
            {
                var available = group.Sum(x => x.Change - x.Held);
                // Preflight also excludes this invoice's linked legacy outbound.
                // Credit its own commitment once, then subtract the current lines once.
                var ownCommitted = group.Where(x => (x.InvoiceHeadId == scope.Header.Id || (legacyOrderId != null && x.LegacyOrderId == legacyOrderId))
                        && (x.Kind == "decrease" || x.Kind == "hold"))
                    .Sum(x => -x.Change + x.Held);
                var existing = required.GetValueOrDefault(group.Key);
                return new PickerBalance(available, existing, Math.Max(0, available + ownCommitted - existing));
            });
        foreach (var id in ids) balances.TryAdd(id, new(0, required.GetValueOrDefault(id), 0));
        return balances;
    }

    private IQueryable<ProductVariant> ActivePickerProducts(int storeId) => _db.ProductVariants.AsNoTracking()
        .Where(x => x.StoreId == storeId && !x.IsDeleted && x.IsActive && x.Product.StoreId == storeId
            && !x.Product.IsDeleted && x.Product.IsActive && x.Product.IsSellable);

    private IQueryable<ProductVariant> MatchingProducts(int storeId, string keyword)
    {
        var query = ActivePickerProducts(storeId);
        var normalized = ProductVariantNameHelper.NormalizeForSearch(keyword);
        if (_db.Database.IsSqlServer())
        {
            const string collation = "Latin1_General_100_CI_AI";
            var names = query.Where(x => (x.ProductVariantNameNormalized != null && x.ProductVariantNameNormalized.Contains(normalized))
                || EF.Functions.Collate(x.ProductVariantName ?? "", collation).Contains(normalized) || x.Sku.Contains(keyword)).Select(x => x.Id);
            var products = query.Where(x => EF.Functions.Collate(x.Product.Name, collation).Contains(normalized)).Select(x => x.Id);
            var barcodes = query.Where(x => x.UnitConversions.Any(c => c.StoreId == storeId && !c.IsDeleted && c.IsActive
                && c.Barcodes.Any(b => b.StoreId == storeId && !b.IsDeleted && b.IsActive && b.Barcode.Contains(keyword)))).Select(x => x.Id);
            var matches = names.Union(products).Union(barcodes);
            return query.Where(x => matches.Contains(x.Id));
        }
        return query.Where(x => (x.ProductVariantName ?? x.Product.Name).Contains(keyword) || x.Product.Name.Contains(keyword)
            || (x.ProductVariantNameNormalized != null && x.ProductVariantNameNormalized.Contains(normalized)) || x.Sku.Contains(keyword)
            || x.UnitConversions.Any(c => c.StoreId == storeId && !c.IsDeleted && c.IsActive
                && c.Barcodes.Any(b => b.StoreId == storeId && !b.IsDeleted && b.IsActive && b.Barcode.Contains(keyword))));
    }

    private static IQueryable<InvoiceProductPickerItemDto> ProjectPicker(IQueryable<ProductVariant> query, int storeId)
        => query.Select(x => new InvoiceProductPickerItemDto
        {
            ProductVariantId = x.Id, ProductId = x.ProductId,
            DisplayName = x.ProductVariantName == null || x.ProductVariantName == "" ? x.Product.Name : x.ProductVariantName,
            Sku = x.Sku, UnitName = x.Product.BaseUnit.Name, UnitPrice = x.Price ?? x.Product.BasePrice,
            Barcode = x.UnitConversions.Where(c => c.StoreId == storeId && !c.IsDeleted && c.IsActive && c.IsBaseUnit && c.Factor == 1)
                .SelectMany(c => c.Barcodes).Where(b => b.StoreId == storeId && !b.IsDeleted && b.IsActive)
                .OrderByDescending(b => b.IsPrimary).ThenBy(b => b.Id).Select(b => b.Barcode).FirstOrDefault()
        });

    private static InvoiceProductPickerPageDto Page(PickerContext scope, IReadOnlyList<InvoiceProductPickerItemDto> items)
        => new() { StockFilterApplied = scope.RequiresStock, WarehouseId = scope.WarehouseId, WarehouseName = scope.WarehouseName, Items = items };

    private static void ApplyBalance(InvoiceProductPickerItemDto item, PickerContext scope, PickerBalance balance)
    {
        item.WarehouseId = scope.WarehouseId; item.WarehouseName = scope.WarehouseName;
        item.AvailableQuantity = balance.Available; item.AlreadyOnInvoiceQuantity = balance.Existing; item.MaximumAdditionalQuantity = balance.Additional;
    }

    private sealed record PickerContext(InvoiceHeaderSnapshot Header, bool RequiresStock, int? WarehouseId, string? WarehouseName);
    private sealed record PickerBalance(decimal Available, decimal Existing, decimal Additional);
}
