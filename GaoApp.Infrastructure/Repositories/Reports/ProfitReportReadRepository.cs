using System.Data;
using System.Linq.Expressions;
using GaoApp.Application.DTOs.Reports.Profit;
using GaoApp.Application.DTOs.Reports.Sales;
using GaoApp.Application.Interfaces.Repositories.Reports;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Reports;

public sealed class ProfitReportReadRepository(AppDbContext db,
    GaoApp.Application.Common.Options.ProfitReportLimits? limits = null) : IProfitReportReadRepository
{
    // Reports use detached evidence, not editable sales documents. Avoid transferring notes,
    // promotion snapshots and other document fields for every sale and return in both periods.
    private static readonly Expression<Func<Order, Order>> ReportOrder = x => new Order {
        Id = x.Id, StoreId = x.StoreId, IsDeleted = x.IsDeleted, OrderNumber = x.OrderNumber,
        Status = x.Status, CompletedAtUtc = x.CompletedAtUtc, POSShiftId = x.POSShiftId,
        Subtotal = x.Subtotal, DiscountTotal = x.DiscountTotal, CustomerId = x.CustomerId,
        GrandTotal = x.GrandTotal, PaidTotal = x.PaidTotal, BalanceDue = x.BalanceDue,
        CustomerPriceTierSnapshot = x.CustomerPriceTierSnapshot, UseMultiLegalEntity = x.UseMultiLegalEntity,
        LegalEntityModeCapturedAtUtc = x.LegalEntityModeCapturedAtUtc, LegalEntityActivationAtUtcSnapshot = x.LegalEntityActivationAtUtcSnapshot
    };
    private static readonly Expression<Func<OrderLine, OrderLine>> ReportLine = x => new OrderLine {
        Id = x.Id, StoreId = x.StoreId, IsDeleted = x.IsDeleted, OrderId = x.OrderId,
        VariantId = x.VariantId, Quantity = x.Quantity, BaseQuantity = x.BaseQuantity,
        Multiplier = x.Multiplier, ItemName = x.ItemName, LineTotal = x.LineTotal, LineCostTotal = x.LineCostTotal,
        StorefrontOrderId = x.StorefrontOrderId, StorefrontOrderLineId = x.StorefrontOrderLineId
    };
    private static readonly Expression<Func<SalesReturn, SalesReturn>> ReportReturn = x => new SalesReturn {
        Id = x.Id, StoreId = x.StoreId, IsDeleted = x.IsDeleted, OrderId = x.OrderId,
        Status = x.Status, CompletedAtUtc = x.CompletedAtUtc, POSShiftId = x.POSShiftId,
        ReturnSubtotal = x.ReturnSubtotal
    };
    private static readonly Expression<Func<SalesReturnLine, SalesReturnLine>> ReportReturnLine = x => new SalesReturnLine {
        Id = x.Id, StoreId = x.StoreId, IsDeleted = x.IsDeleted, SalesReturnId = x.SalesReturnId,
        OrderLineId = x.OrderLineId, VariantId = x.VariantId, Action = x.Action,
        ReturnBaseQuantity = x.ReturnBaseQuantity, ItemName = x.ItemName, RefundLineTotal = x.RefundLineTotal
    };
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
            .Select(ReportOrder)
            .ReadWithinBudgetAsync(budget, ct);
        var returnsInPeriod = await db.SalesReturns.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.StoreId == storeId && x.CompletedAtUtc >= from && x.CompletedAtUtc < to)
            .Select(ReportReturn)
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
            var nativeTransactions = parents.Where(x => x.ReferenceType == InventoryReferenceType.Delivery).Select(x => x.InventoryTransactionId).Distinct().ToArray();
            foreach (var batch in nativeTransactions.Chunk(1000))
                foreach (var id in await db.OrderLegalEntityAllocations.IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId == storeId && x.InventoryTransactionId.HasValue && batch.Contains(x.InventoryTransactionId.Value) &&
                    db.Set<StorefrontSaleBinding>().IgnoreQueryFilters().Any(b => b.StoreId == storeId && b.SaleOrderId == x.OrderId)).Select(x => x.OrderId).Distinct().ToListAsync(ct)) orderIds.Add(id);
        }
        foreach (var batch in orderIds.Except(orders.Select(x => x.Id)).ToArray().Chunk(1000))
            orders.AddRange(await db.Orders.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && batch.Contains(x.Id)).Select(ReportOrder).ReadWithinBudgetAsync(budget, ct));
        snapshot.Orders = orders;
        var entries = new List<InventoryValuationEntry>();
        foreach (var batch in orderIds.ToArray().Chunk(1000))
        {
            snapshot.Lines.AddRange(await db.OrderLines.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && batch.Contains(x.OrderId)).Select(ReportLine).ReadWithinBudgetAsync(budget, ct));
            snapshot.Returns.AddRange(await db.SalesReturns.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && batch.Contains(x.OrderId)).Select(ReportReturn).ReadWithinBudgetAsync(budget, ct));
            snapshot.LegalAllocations.AddRange(await db.OrderLegalEntityAllocations.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && batch.Contains(x.OrderId)).ReadWithinBudgetAsync(budget, ct));
            snapshot.StorefrontSales.AddRange(await db.Set<StorefrontSaleBinding>().IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId == storeId && batch.Contains(x.SaleOrderId)).ReadWithinBudgetAsync(budget, ct));
            snapshot.StorefrontSettlements.AddRange(await db.Set<StorefrontSettlementLink>().IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId == storeId && batch.Contains(x.SaleOrderId)).ReadWithinBudgetAsync(budget, ct));
            snapshot.LegalReversals.AddRange(await db.OrderLegalEntityAllocationReversals.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && batch.Contains(x.OrderId)).ReadWithinBudgetAsync(budget, ct));
            snapshot.DeliveryJournals.AddRange(await db.DeliveryJournalEntries.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && x.Kind == DeliveryJournalKind.Settlement &&
                    x.SaleOrderId.HasValue && batch.Contains(x.SaleOrderId.Value)).ReadWithinBudgetAsync(budget, ct));
            var references = batch.Select(x => x.ToString()).ToArray();
            var transactions = db.InventoryTransactions.IgnoreQueryFilters()
                .Where(x => x.StoreId == storeId && x.ReferenceType == InventoryReferenceType.Order &&
                    references.Contains(x.ReferenceId)).Select(x => x.Id);
            var byOrder = db.InventoryValuationEntries.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && x.ReferenceType == InventoryReferenceType.Order &&
                    references.Contains(x.ReferenceId));
            var byTransaction = db.InventoryValuationEntries.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && transactions.Contains(x.InventoryTransactionId));
            // Deduplicate narrow identities before fetching the wide valuation rows. Both paths are
            // required: a malformed order reference remains evidence through its transaction link.
            var entryIds = byOrder.Select(x => x.Id).Union(byTransaction.Select(x => x.Id));
            entries.AddRange(await db.InventoryValuationEntries.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && entryIds.Contains(x.Id)).ReadWithinBudgetAsync(budget, ct));
        }
        var returnIds = snapshot.Returns.Select(x => x.Id).ToArray();
        foreach (var batch in returnIds.Chunk(1000))
        {
            snapshot.ReturnLines.AddRange(await db.SalesReturnLines.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && batch.Contains(x.SalesReturnId)).Select(ReportReturnLine).ReadWithinBudgetAsync(budget, ct));
            var references = batch.Select(x => x.ToString()).ToArray();
            entries.AddRange(await db.InventoryValuationEntries.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && x.ReferenceType == InventoryReferenceType.Refund &&
                    references.Contains(x.ReferenceId)).ReadWithinBudgetAsync(budget, ct));
        }
        // Delivered sales reuse their original dispatch. There is deliberately no second POS
        // stock issue; load the immutable handover and its dispatch evidence in the same snapshot.
        foreach (var batch in snapshot.StorefrontSales.Select(x => x.StorefrontOrderId).Distinct().ToArray().Chunk(1000))
        {
            snapshot.StorefrontOrders.AddRange(await db.StorefrontOrders.IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId == storeId && batch.Contains(x.Id)).ReadWithinBudgetAsync(budget, ct));
            snapshot.StorefrontLines.AddRange(await db.Set<StorefrontOrderLine>().IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId == storeId && batch.Contains(x.StorefrontOrderId)).ReadWithinBudgetAsync(budget, ct));
            snapshot.StorefrontSources.AddRange(await db.Set<StorefrontFulfillmentSource>().IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId == storeId && batch.Contains(x.StorefrontOrderId)).ReadWithinBudgetAsync(budget, ct));
            snapshot.StorefrontLineSources.AddRange(await db.Set<StorefrontLineSource>().IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId == storeId && batch.Contains(x.StorefrontOrderId)).ReadWithinBudgetAsync(budget, ct));
            snapshot.StorefrontDeliveryLines.AddRange(await db.DeliveryOrderLines.IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId == storeId && x.StorefrontOrderId.HasValue && batch.Contains(x.StorefrontOrderId.Value)).ReadWithinBudgetAsync(budget, ct));
        }
        snapshot.StorefrontServices = await db.Set<StorefrontServiceRevenue>().IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId == storeId && x.RecognizedAtUtc >= from && x.RecognizedAtUtc < to).ReadWithinBudgetAsync(budget, ct);
        foreach (var batch in snapshot.DeliveryJournals.Select(x => x.DeliveryOrderId).Concat(snapshot.StorefrontSources.Select(x => x.DeliveryOrderId).OfType<int>()).Distinct().ToArray().Chunk(1000))
        {
            snapshot.Deliveries.AddRange(await db.DeliveryOrders.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && batch.Contains(x.Id)).Select(x => new DeliveryOrder {
                    Id = x.Id, StoreId = x.StoreId, IsDeleted = x.IsDeleted, State = x.State, Revision = x.Revision,
                    SourceWarehouseId = x.SourceWarehouseId, SourceLegalEntityId = x.SourceLegalEntityId,
                    SourceCartId = x.SourceCartId, CustomerId = x.CustomerId, SourceKind = x.SourceKind,
                    StorefrontOrderId = x.StorefrontOrderId, StorefrontFulfillmentSourceId = x.StorefrontFulfillmentSourceId
                }).ReadWithinBudgetAsync(budget, ct));
            snapshot.DeliveryRevisions.AddRange(await db.DeliveryRevisions.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && batch.Contains(x.DeliveryOrderId) && x.Action == "counter-handover")
                .ReadWithinBudgetAsync(budget, ct));
            snapshot.DeliveryFragments.AddRange(await db.DeliveryDispatchCostFragments.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && batch.Contains(x.DeliveryOrderId)).ReadWithinBudgetAsync(budget, ct));
            snapshot.DeliveryJournals.AddRange(await db.DeliveryJournalEntries.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && batch.Contains(x.DeliveryOrderId) && x.Kind != DeliveryJournalKind.Settlement)
                .ReadWithinBudgetAsync(budget, ct));
        }
        foreach (var batch in snapshot.DeliveryFragments.Select(x => x.InventoryValuationEntryId).Distinct().ToArray().Chunk(1000))
            entries.AddRange(await db.InventoryValuationEntries.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && batch.Contains(x.Id)).ReadWithinBudgetAsync(budget, ct));
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
        var debits = new List<InventoryCostBudgetDebit>();
        foreach (var batch in snapshot.Entries.Select(x => x.Id).ToArray().Chunk(1000))
            debits.AddRange(await db.InventoryCostBudgetDebits.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && batch.Contains(x.InventoryValuationEntryId)).ReadWithinBudgetAsync(budget, ct));
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
                     .Concat(debits.Select(x => x.InventoryCostLayerId)).Distinct().ToArray().Chunk(1000))
            layers.AddRange(await db.InventoryCostLayers.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == storeId && batch.Contains(x.Id)).ReadWithinBudgetAsync(budget, ct));
        var txMap = txs.ToDictionary(x => x.Id);
        var whMap = warehouses.ToDictionary(x => x.Id);
        var varMap = variants.ToDictionary(x => x.Id);
        var layerMap = layers.ToDictionary(x => x.Id);
        var allocationMap = allocations.ToLookup(x => x.InventoryValuationEntryId);
        var debitMap = debits.ToLookup(x => x.InventoryValuationEntryId);
        var allocationIds = allocations.ToLookup(x => (x.InventoryValuationEntryId, x.Id));
        foreach (var entry in snapshot.Entries)
        {
            ct.ThrowIfCancellationRequested();
            entry.InventoryTransaction = txMap.GetValueOrDefault(entry.InventoryTransactionId)!;
            entry.Warehouse = whMap.GetValueOrDefault(entry.WarehouseId)!;
            entry.ProductVariant = varMap.GetValueOrDefault(entry.ProductVariantId)!;
            entry.InventoryCostLayer = entry.InventoryCostLayerId is int id ? layerMap.GetValueOrDefault(id) : null;
            entry.CostLayerAllocations = allocationMap[entry.Id].ToList();
            entry.CostBudgetDebits = debitMap[entry.Id].ToList();
            foreach (var debit in entry.CostBudgetDebits)
            {
                debit.Layer = layerMap.GetValueOrDefault(debit.InventoryCostLayerId)!;
                debit.Allocation = allocationIds[(entry.Id, debit.InventoryCostLayerAllocationId)].SingleOrDefault()!;
                debit.ValuationEntry = entry;
            }
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
