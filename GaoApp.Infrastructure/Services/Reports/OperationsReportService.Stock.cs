using GaoApp.Application.DTOs.Reports;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Inventory;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Reports;

public sealed partial class OperationsReportService
{
    public async Task<StockReportDto> StockAsync(OperationsReportQueryDto query, CancellationToken ct)
    {
        var p = Period(query);
        return await Snapshot(async () => {
            var warehouses = await db.Warehouses.IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId == StoreId).Select(x => new { x.Id, x.Name }).ToListAsync(ct);
            if (query.WarehouseId != null && warehouses.All(x => x.Id != query.WarehouseId)) throw new KeyNotFoundException("Không tìm thấy kho trong cửa hàng.");
            var canCost = await InventoryCostReadAccess.CanViewAsync(db, StoreId, db.CurrentUserId, ct);
            var movements = await Bounded(db.InventoryTransactions.AsNoTracking().Where(x => x.StoreId == StoreId && x.OccurredAtUtc < p.ToUtcExclusive
                && (query.WarehouseId == null || x.WarehouseId == query.WarehouseId))
                .Select(x => new { x.WarehouseId, x.ProductVariantId, x.Id, x.OccurredAtUtc, x.BeforeQty, x.AfterQty, x.QuantityChange, x.TransactionType,
                    x.IsProvisionalCost, x.CostFinalizedAtUtc }), ct);
            var balances = await Bounded(db.InventoryBalances.AsNoTracking().Where(x => x.StoreId == StoreId && x.CreatedAtUtc < p.ToUtcExclusive && (query.WarehouseId == null || x.WarehouseId == query.WarehouseId))
                .Select(x => new { x.WarehouseId, x.ProductVariantId, x.OnHandQty }), ct);
            var values = canCost ? await Bounded(db.InventoryValuationEntries.AsNoTracking().Where(x => x.StoreId == StoreId && x.OccurredAtUtc < p.ToUtcExclusive
                && (query.WarehouseId == null || x.WarehouseId == query.WarehouseId))
                .Select(x => new { x.WarehouseId, x.ProductVariantId, x.Id, x.InventoryTransactionId, x.OccurredAtUtc, x.Amount, x.Quantity, x.RunningValueAfter, x.IsProvisional, x.CostFinalizedAtUtc }), ct) : [];
            var metadata = await Bounded((from variant in db.ProductVariants.IgnoreQueryFilters().AsNoTracking()
                join product in db.Products.IgnoreQueryFilters().AsNoTracking() on variant.ProductId equals product.Id
                join category in db.Categories.IgnoreQueryFilters().AsNoTracking() on product.CategoryId equals category.Id
                join unit in db.Units.IgnoreQueryFilters().AsNoTracking() on product.BaseUnitId equals unit.Id
                where variant.StoreId == StoreId && product.StoreId == StoreId && category.StoreId == StoreId && unit.StoreId == StoreId
                select new { variant.Id, Name = product.Name + (variant.ProductVariantName == null ? "" : " · " + variant.ProductVariantName), variant.Sku, Category = category.Name, Unit = unit.Name }), ct);
            var names = metadata.ToDictionary(x => x.Id);
            var warehouseNames = warehouses.ToDictionary(x => x.Id, x => x.Name);
            var movementGroups = movements.ToLookup(x => (x.WarehouseId, x.ProductVariantId));
            var valuationGroups = values.ToLookup(x => (x.WarehouseId, x.ProductVariantId));
            var balanceMap = balances.ToDictionary(x => (x.WarehouseId, x.ProductVariantId), x => x.OnHandQty);
            var keys = movements.Select(x => (x.WarehouseId, x.ProductVariantId)).Concat(balances.Select(x => (x.WarehouseId, x.ProductVariantId))).Distinct();
            var rows = new List<StockReportRowDto>();
            var days = (p.ToDate - p.FromDate).Days + 1;
            foreach (var key in keys) {
                var history = movementGroups[key].OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.Id).ToList();
                var inPeriod = history.Where(x => x.OccurredAtUtc >= p.FromUtc).ToList();
                var valuation = valuationGroups[key].OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.Id).ToList();
                var missing = history.Count == 0;
                decimal? opening = missing ? null : history[0].BeforeQty + history.Where(x => x.OccurredAtUtc < p.FromUtc).Sum(x => x.QuantityChange);
                var incoming = inPeriod.Where(x => x.QuantityChange > 0).Sum(x => x.QuantityChange);
                var outgoing = -inPeriod.Where(x => x.QuantityChange < 0).Sum(x => x.QuantityChange);
                decimal? closing = missing ? p.ToDate == DateTime.UtcNow.AddHours(7).Date ? balanceMap.GetValueOrDefault(key) : null : opening + incoming - outgoing;
                // Missing valuation fragments must never silently become a zero inventory value.
                var valuedMovementIds = valuation.Select(x => x.InventoryTransactionId).ToHashSet();
                var incompleteValue = history.Any(x => x.QuantityChange != 0 && !valuedMovementIds.Contains(x.Id));
                var provisional = history.Any(x => x.IsProvisionalCost && x.CostFinalizedAtUtc == null || x.CostFinalizedAtUtc >= p.ToUtcExclusive);
                decimal? openingValue = canCost && valuation.Count > 0 && !incompleteValue
                    ? valuation[0].RunningValueAfter - valuation[0].Amount + valuation.Where(x => x.OccurredAtUtc < p.FromUtc).Sum(x => x.Amount) : null;
                decimal? closingValue = openingValue + valuation.Where(x => x.OccurredAtUtc >= p.FromUtc).Sum(x => x.Amount);
                var sold = -inPeriod.Where(x => x.TransactionType == InventoryTransactionType.SaleIssue).Sum(x => Math.Min(0, x.QuantityChange));
                var lastSold = history.Where(x => x.TransactionType == InventoryTransactionType.SaleIssue && x.QuantityChange < 0).Select(x => (DateTime?)x.OccurredAtUtc).LastOrDefault();
                var state = missing ? "incomplete" : closing < 0 ? "negative" : closing == 0 ? "empty" : closing <= query.LowStockThreshold ? "low" : sold == 0 ? "slow" : "normal";
                names.TryGetValue(key.ProductVariantId, out var meta);
                rows.Add(new(key.WarehouseId, key.ProductVariantId, warehouseNames.GetValueOrDefault(key.WarehouseId, $"Kho #{key.WarehouseId}"),
                    meta?.Name ?? $"Sản phẩm #{key.ProductVariantId}", meta?.Sku ?? "", meta?.Category ?? "Chưa xác định", meta?.Unit ?? "Đơn vị gốc",
                    opening, incoming, outgoing, closing, openingValue, closingValue, sold, lastSold,
                    sold > 0 && closing >= 0 ? decimal.Round(closing.Value * days / sold, 1) : null, state, provisional, missing || canCost && incompleteValue));
            }
            var warnings = new List<string>();
            if (rows.Any(x => x.IsIncomplete)) warnings.Add("Có mặt hàng thiếu lịch sử số lượng hoặc định giá. Tổng giá trị chưa xác định khi chưa đủ sổ kho; số lượng hiện tại chỉ dùng cho ngày hôm nay và có nhãn riêng.");
            if (rows.Any(x => x.IsProvisional)) warnings.Add("Giá trị có phần tạm tính. Kỳ lịch sử tính từ sổ định giá tại mốc cuối kỳ, không dùng giá vốn hiện tại thay thế.");
            if (!canCost) warnings.Add("Giá trị tồn kho chỉ hiển thị cho chủ cửa hàng có quyền xem giá vốn theo chính sách hiện tại.");
            warnings.Add("Nhập/xuất gồm bán, trả, chuyển kho, kiểm kê và điều chỉnh. Số lượng trình bày riêng theo đơn vị gốc từng sản phẩm; không cộng các đơn vị khác nhau.");
            return new StockReportDto(p, warehouses.Select(x => new ReportOptionDto(x.Id.ToString(), x.Name)).ToList(), rows.OrderByDescending(x => x.ClosingValue).ThenBy(x => x.Name).ToList(),
                canCost && rows.All(x => x.ClosingValue.HasValue) ? rows.Sum(x => x.ClosingValue!.Value) : null,
                rows.Count(x => x.Closing < 0), rows.Count(x => x.Closing >= 0 && x.Closing <= query.LowStockThreshold),
                rows.Count(x => x.Closing > 0 && x.Sold == 0), rows.Count(x => x.IsProvisional), canCost, warnings);
        }, ct);
    }
}
