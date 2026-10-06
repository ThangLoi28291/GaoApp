using System.Data;
using GaoApp.Application.DTOs.Reports.Profit;
using GaoApp.Application.DTOs.Reports.Sales;
using GaoApp.Application.Interfaces.Repositories.Reports;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Reports;

public sealed class ProfitReportReadRepository(AppDbContext db,
    GaoApp.Application.Common.Options.ProfitReportLimits? limits = null) : IProfitReportReadRepository
{
    public async Task<ProfitSourceSnapshot> ReadAsync(int storeId, SalesResolvedPeriodSet periods,
        bool activity, CancellationToken ct = default)
    {
        if (storeId <= 0 || db.CurrentStoreId != storeId)
            throw new InvalidOperationException("Báo cáo yêu cầu cửa hàng hiện tại hợp lệ.");
        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Profit reads require their own consistent read transaction.");
        if (!db.Database.IsRelational())
            return await MaterializeAsync(storeId, periods, activity, ct);
        // One strategy unit: a retry discards the complete failed read, never mixes generations.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // Transaction-wide row versions preserve one generation without retaining shared data locks.
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Snapshot, ct);
            var snapshot = await MaterializeAsync(storeId, periods, activity, ct);
            await transaction.CommitAsync(ct);
            return snapshot;
        });
    }

    private async Task<ProfitSourceSnapshot> MaterializeAsync(int storeId, SalesResolvedPeriodSet periods,
        bool activity, CancellationToken ct)
    {
        var budget = new ProfitReportRowBudget(limits?.MaxSourceRows ?? 100000);
        var from = periods.Comparison?.Period.FromUtc ?? periods.Current.Period.FromUtc;
        var to = periods.Current.Period.ToUtcExclusive;
        var snapshot = new ProfitSourceSnapshot { StoreId = storeId, ReadAtUtc = DateTime.UtcNow };
        snapshot.Terminals = await db.POSTerminals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.StoreId == storeId && !x.IsDeleted)
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new SalesTerminalOptionDto { Id = x.Id, Name = x.Name, Code = x.Code }).ReadWithinBudgetAsync(budget, ct);
        if (periods.Current.TerminalId is int terminal && !snapshot.Terminals.Any(x => x.Id == terminal))
            throw new ArgumentException("Terminal không thuộc cửa hàng hiện tại.");

        // Do not inner-join required navigation: missing masters must remain detectable.
        var orders = await db.Orders.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.StoreId == storeId && x.CompletedAtUtc >= from && x.CompletedAtUtc < to)
            .ReadWithinBudgetAsync(budget, ct);
        var returnsInPeriod = await db.SalesReturns.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.StoreId == storeId && x.CompletedAtUtc >= from && x.CompletedAtUtc < to)
            .ReadWithinBudgetAsync(budget, ct);
        var orderIds = orders.Select(x => x.Id).Concat(returnsInPeriod.Select(x => x.OrderId)).ToHashSet();
        var activityEntries = new List<InventoryValuationEntry>();
        if (activity)
        {
            activityEntries = await db.InventoryValuationEntries.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && x.EntryType == InventoryValuationEntryType.Revaluation &&
                    x.OccurredAtUtc >= periods.Current.Period.FromUtc && x.OccurredAtUtc < to).ReadWithinBudgetAsync(budget, ct);
            snapshot.ActivityEntryIds = activityEntries.Select(x => x.Id).ToHashSet();
            var rootIds = activityEntries.SelectMany(x => new[] { x.RevaluationOfEntryId, x.SourceValuationEntryId })
                .OfType<int>().Distinct().ToArray();
            var parents = new List<InventoryValuationEntry>();
            foreach (var batch in rootIds.Chunk(1000))
                parents.AddRange(await db.InventoryValuationEntries.IgnoreQueryFilters().AsNoTracking()
                    .Where(x => x.StoreId == storeId && batch.Contains(x.Id)).ReadWithinBudgetAsync(budget, ct));
            foreach (var entry in activityEntries.Concat(parents))
                if (entry.ReferenceType == InventoryReferenceType.Order &&
                    int.TryParse(entry.ReferenceId, out var id)) orderIds.Add(id);
        }
        foreach (var batch in orderIds.Except(orders.Select(x => x.Id)).ToArray().Chunk(1000))
            orders.AddRange(await db.Orders.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && batch.Contains(x.Id)).ReadWithinBudgetAsync(budget, ct));
        snapshot.Orders = orders;
        var entries = new List<InventoryValuationEntry>();
        foreach (var batch in orderIds.ToArray().Chunk(1000))
        {
            snapshot.Lines.AddRange(await db.OrderLines.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && batch.Contains(x.OrderId)).ReadWithinBudgetAsync(budget, ct));
            snapshot.Returns.AddRange(await db.SalesReturns.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && batch.Contains(x.OrderId)).ReadWithinBudgetAsync(budget, ct));
            snapshot.LegalAllocations.AddRange(await db.OrderLegalEntityAllocations.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && batch.Contains(x.OrderId)).ReadWithinBudgetAsync(budget, ct));
            snapshot.LegalReversals.AddRange(await db.OrderLegalEntityAllocationReversals.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && batch.Contains(x.OrderId)).ReadWithinBudgetAsync(budget, ct));
            var references = batch.Select(x => x.ToString()).ToArray();
            var transactions = db.InventoryTransactions.IgnoreQueryFilters()
                .Where(x => x.StoreId == storeId && x.ReferenceType == InventoryReferenceType.Order &&
                    references.Contains(x.ReferenceId)).Select(x => x.Id);
            var byOrder = db.InventoryValuationEntries.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && x.ReferenceType == InventoryReferenceType.Order &&
                    references.Contains(x.ReferenceId));
            var byTransaction = db.InventoryValuationEntries.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && transactions.Contains(x.InventoryTransactionId));
            // Separate searchable branches instead of an OR around a transaction subquery.
            // UNION retains malformed reference evidence linked by transaction and removes duplicates in SQL.
            entries.AddRange(await byOrder.Union(byTransaction).ReadWithinBudgetAsync(budget, ct));
        }
        var returnIds = snapshot.Returns.Select(x => x.Id).ToArray();
        foreach (var batch in returnIds.Chunk(1000))
        {
            snapshot.ReturnLines.AddRange(await db.SalesReturnLines.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && batch.Contains(x.SalesReturnId)).ReadWithinBudgetAsync(budget, ct));
            var references = batch.Select(x => x.ToString()).ToArray();
            entries.AddRange(await db.InventoryValuationEntries.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && x.ReferenceType == InventoryReferenceType.Refund &&
                    references.Contains(x.ReferenceId)).ReadWithinBudgetAsync(budget, ct));
        }
        // Closure ignores report dates. Follow both link columns, including malformed children.
        foreach (var batch in entries.Where(x => x.EntryType == InventoryValuationEntryType.Outbound)
                     .Select(x => x.Id).Distinct().ToArray().Chunk(1000))
            entries.AddRange(await db.InventoryValuationEntries.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId &&
                    ((x.SourceValuationEntryId.HasValue && batch.Contains(x.SourceValuationEntryId.Value)) ||
                     (x.RevaluationOfEntryId.HasValue && batch.Contains(x.RevaluationOfEntryId.Value)))).ReadWithinBudgetAsync(budget, ct));
        snapshot.Entries = entries.Concat(activityEntries).DistinctBy(x => x.Id).ToList();

        var txs = new List<InventoryTransaction>();
        var warehouses = new List<Warehouse>();
        var variants = new List<ProductVariant>();
        var layers = new List<InventoryCostLayer>();
        var allocations = new List<InventoryCostLayerAllocation>();
        foreach (var batch in snapshot.Entries.Select(x => x.Id).ToArray().Chunk(1000))
            allocations.AddRange(await db.InventoryCostLayerAllocations.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && batch.Contains(x.InventoryValuationEntryId)).ReadWithinBudgetAsync(budget, ct));
        foreach (var batch in snapshot.Entries.Select(x => x.InventoryTransactionId).Distinct().ToArray().Chunk(1000))
            txs.AddRange(await db.InventoryTransactions.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && batch.Contains(x.Id)).ReadWithinBudgetAsync(budget, ct));
        foreach (var batch in snapshot.Entries.Select(x => x.WarehouseId).Distinct().ToArray().Chunk(1000))
            warehouses.AddRange(await db.Warehouses.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && batch.Contains(x.Id)).ReadWithinBudgetAsync(budget, ct));
        foreach (var batch in snapshot.Entries.Select(x => x.ProductVariantId).Distinct().ToArray().Chunk(1000))
            variants.AddRange(await db.ProductVariants.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && batch.Contains(x.Id)).ReadWithinBudgetAsync(budget, ct));
        foreach (var batch in snapshot.Entries.Select(x => x.InventoryCostLayerId).OfType<int>()
                     .Distinct().ToArray().Chunk(1000))
            layers.AddRange(await db.InventoryCostLayers.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && batch.Contains(x.Id)).ReadWithinBudgetAsync(budget, ct));
        var txMap = txs.ToDictionary(x => x.Id);
        var whMap = warehouses.ToDictionary(x => x.Id);
        var varMap = variants.ToDictionary(x => x.Id);
        var layerMap = layers.ToDictionary(x => x.Id);
        var allocationMap = allocations.ToLookup(x => x.InventoryValuationEntryId);
        foreach (var entry in snapshot.Entries)
        {
            entry.InventoryTransaction = txMap.GetValueOrDefault(entry.InventoryTransactionId)!;
            entry.Warehouse = whMap.GetValueOrDefault(entry.WarehouseId)!;
            entry.ProductVariant = varMap.GetValueOrDefault(entry.ProductVariantId)!;
            entry.InventoryCostLayer = entry.InventoryCostLayerId is int id ? layerMap.GetValueOrDefault(id) : null;
            entry.CostLayerAllocations = allocationMap[entry.Id].ToList();
        }
        foreach (var batch in orders.Select(x => x.POSShiftId).Concat(snapshot.Returns.Select(x => x.POSShiftId))
                     .Distinct().ToArray().Chunk(1000))
            snapshot.Shifts.AddRange(await db.POSShifts.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && batch.Contains(x.Id)).ReadWithinBudgetAsync(budget, ct));
        foreach (var batch in snapshot.LegalAllocations.Select(x => x.LegalEntityId).Distinct().ToArray().Chunk(1000))
            snapshot.LegalEntities.AddRange(await db.LegalEntities.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && batch.Contains(x.Id)).ReadWithinBudgetAsync(budget, ct));
        if (periods.IncludeManagement)
        {
            foreach (var batch in orders.Select(x => x.CustomerId).OfType<int>().Distinct().ToArray().Chunk(1000))
                snapshot.Customers.AddRange(await db.Customers.IgnoreQueryFilters().AsNoTracking()
                    .Where(x => x.StoreId == storeId && batch.Contains(x.Id))
                    .Select(x => new GaoApp.Application.DTOs.Reports.ReportCustomerInfo(x.Id, x.Name, x.PriceTier)).ReadWithinBudgetAsync(budget, ct));
            foreach (var batch in snapshot.Lines.Select(x => x.VariantId).Concat(snapshot.ReturnLines.Select(x => x.VariantId)).Distinct().ToArray().Chunk(1000))
                snapshot.Products.AddRange(await (from v in db.ProductVariants.IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId == storeId && batch.Contains(x.Id))
                    join p in db.Products.IgnoreQueryFilters().Where(x => x.StoreId == storeId) on v.ProductId equals p.Id into ps
                    from p in ps.DefaultIfEmpty()
                    join c in db.Categories.IgnoreQueryFilters().Where(x => x.StoreId == storeId) on (p == null ? 0 : p.CategoryId) equals c.Id into cs
                    from c in cs.DefaultIfEmpty()
                    select new GaoApp.Application.DTOs.Reports.ReportProductInfo(v.Id, v.ProductVariantName ?? (p == null ? "Sản phẩm không còn trong danh mục" : p.Name), v.Sku,
                        c == null ? 0 : c.Id, c == null ? "Chưa phân nhóm" : c.Name)).ReadWithinBudgetAsync(budget, ct));
            var localFrom = periods.Comparison?.Period.FromDate ?? periods.Current.Period.FromDate;
            var localTo = periods.Current.Period.ToDate;
            snapshot.OperatingExpenses = await db.OperatingExpenses.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && !x.IsDeleted && x.RecognitionFrom <= localTo && x.RecognitionTo >= localFrom)
                .ReadWithinBudgetAsync(budget, ct);
        }
        return snapshot;
    }
}
