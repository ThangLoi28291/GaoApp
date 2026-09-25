using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Orders;

internal interface IOrderLegalEntitySalesReturnBatchService
{
    Task<OrderLegalEntityReversalService.PreparedSalesReturnBatch>
        PrepareSalesReturnBatchAsync(
            Order order,
            SalesReturn salesReturn,
            IReadOnlyCollection<SalesReturnLine> salesReturnLines,
            CancellationToken ct = default);

    Task ApplyPreparedSalesReturnLineAsync(
        OrderLegalEntityReversalService.PreparedSalesReturnBatch batch,
        int salesReturnLineId,
        CancellationToken ct = default);
}

/// <summary>
/// Orchestrator reversal của order đã allocation. Không suy đoán lại SalePriority và
/// không dùng kho ca POS; mọi fragment phải quay về đúng allocation/valuation gốc.
/// Caller sở hữu transaction ngoài cùng.
/// </summary>
public sealed class OrderLegalEntityReversalService
    : IOrderLegalEntityReversalService,
      IOrderLegalEntitySalesReturnBatchService
{
    private const decimal QuantityTolerance = 0.0001m;

    private readonly IOrderLegalEntityAllocationRepository _allocations;
    private readonly IOrderLegalEntityAllocationReversalRepository _reversals;
    private readonly IReturnableValuationFragmentService _returnableFragments;
    private readonly IReturnCostAllocator _returnCostAllocator;
    private readonly IInventoryMovementService _movements;
    private readonly IInventoryMovementFactory _movementFactory;

    public OrderLegalEntityReversalService(
        IOrderLegalEntityAllocationRepository allocations,
        IOrderLegalEntityAllocationReversalRepository reversals,
        IReturnableValuationFragmentService returnableFragments,
        IReturnCostAllocator returnCostAllocator,
        IInventoryMovementService movements,
        IInventoryMovementFactory movementFactory)
    {
        _allocations = allocations;
        _reversals = reversals;
        _returnableFragments = returnableFragments;
        _returnCostAllocator = returnCostAllocator;
        _movements = movements;
        _movementFactory = movementFactory;
    }

    public async Task<bool> ReverseVoidIfAllocatedAsync(
        Order order,
        string reason,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        var persistedAllocations = await _allocations.GetForOrderAsync(order.Id, ct);
        if (persistedAllocations.Count == 0)
            return false;

        if (_returnableFragments is not IReturnableValuationCostEvidence costEvidence)
            throw new InvalidOperationException("Void requires durable cost closure validation.");

        var allocationByTransaction = BuildAllocationMap(order, persistedAllocations);
        var plans = new List<ReversalPlan>();

        foreach (var line in order.Lines.Where(x => !x.IsDeleted).OrderBy(x => x.Id))
        {
            var baseQuantity = line.BaseQuantity > 0
                ? line.BaseQuantity
                : line.Quantity * (line.Multiplier <= 0 ? 1m : line.Multiplier);
            var fragments = await _returnableFragments.GetForOrderLineAsync(
                order.Id,
                line.Id,
                ct);

            var costAllocations = _returnCostAllocator.Allocate(fragments, baseQuantity);
            plans.AddRange(BuildPlans(
                line,
                fragments,
                costAllocations,
                allocationByTransaction));
        }

        AllocateVoidFinancialAmounts(plans);
        var balanceKeys = plans
            .Select(x => new InventoryPostingLockKey(
                order.StoreId,
                x.Source.WarehouseId,
                x.Source.ProductVariantId))
            .Distinct()
            .ToList();
        if (balanceKeys.Count > 0)
        {
            await _movements.PreLockBalancesAsync(balanceKeys, ct);
        }

        var refreshedPlans = new List<ReversalPlan>();
        foreach (var line in order.Lines.Where(x => !x.IsDeleted).OrderBy(x => x.Id))
        {
            var fragments = await _returnableFragments.GetForOrderLineAsync(order.Id, line.Id, ct);
            var quantity = plans.Where(x => x.OrderLine.Id == line.Id).Sum(x => x.BaseQuantity);
            refreshedPlans.AddRange(BuildPlans(line, fragments,
                _returnCostAllocator.Allocate(fragments, quantity), allocationByTransaction));
        }
        EnsureSamePlanIdentity(plans, refreshedPlans);
        plans = refreshedPlans;
        AllocateVoidFinancialAmounts(plans);

        var reversalRows = new List<OrderLegalEntityAllocationReversal>();
        var occurredAtUtc = DateTime.UtcNow;

        foreach (var plan in plans)
        {
            var request = _movementFactory.CreateSaleVoid(
                plan.Source.WarehouseId,
                plan.Source.ProductVariantId,
                order.Id,
                plan.OrderLine.Id,
                plan.BaseQuantity,
                plan.UnitCost,
                reason,
                occurredAtUtc);
            request.ReferenceSubKey =
                $"LE-VOID:A{plan.Allocation.Id}:S{plan.Source.SourceValuationEntryId}";
            request.SourceValuationEntryId = plan.Source.SourceValuationEntryId;
            request.SourceReferenceSubKey = plan.Source.ReferenceSubKey;

            var movement = await _movements.CreateAsync(request, ct);
            EnsureMovementCreated(movement, "void", order.Id, plan.Source.SourceValuationEntryId);

            reversalRows.Add(BuildReversal(
                order,
                plan,
                OrderLegalEntityReversalType.Void,
                plan.FinancialAmount,
                movement.InventoryTransactionId,
                salesReturn: null,
                salesReturnLine: null,
                reason,
                occurredAtUtc));
        }

        await _reversals.AddRangeAsync(reversalRows, ct);
        foreach (var line in order.Lines.Where(x => !x.IsDeleted))
            await costEvidence.ValidateVoidClosureAsync(order.Id, line.Id, ct);
        return true;
    }

    public async Task<bool> ReverseSalesReturnLineIfAllocatedAsync(
        Order order,
        SalesReturn salesReturn,
        SalesReturnLine salesReturnLine,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(salesReturn);
        ArgumentNullException.ThrowIfNull(salesReturnLine);

        var batch = await PrepareSalesReturnBatchAsync(
            order,
            salesReturn,
            [salesReturnLine],
            ct);
        if (!batch.HandledLineIds.Contains(salesReturnLine.Id))
            return false;

        if (batch.LockKeys.Count > 0)
        {
            await _movements.PreLockBalancesAsync(batch.LockKeys, ct);
        }

        await ApplySalesReturnBatchAsync(batch, ct);
        return true;
    }

    async Task<PreparedSalesReturnBatch>
        IOrderLegalEntitySalesReturnBatchService.PrepareSalesReturnBatchAsync(
            Order order,
            SalesReturn salesReturn,
            IReadOnlyCollection<SalesReturnLine> salesReturnLines,
            CancellationToken ct)
        => await PrepareSalesReturnBatchAsync(
            order,
            salesReturn,
            salesReturnLines,
            ct);

    Task IOrderLegalEntitySalesReturnBatchService
        .ApplyPreparedSalesReturnLineAsync(
            PreparedSalesReturnBatch batch,
            int salesReturnLineId,
            CancellationToken ct)
        => ApplyPreparedSalesReturnLineAsync(
            batch,
            salesReturnLineId,
            ct);

    internal async Task<PreparedSalesReturnBatch> PrepareSalesReturnBatchAsync(
        Order order,
        SalesReturn salesReturn,
        IReadOnlyCollection<SalesReturnLine> salesReturnLines,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(salesReturn);
        ArgumentNullException.ThrowIfNull(salesReturnLines);

        var persistedAllocations = await _allocations.GetForOrderAsync(order.Id, ct);
        var allocationByTransaction = persistedAllocations.Count == 0
            ? new Dictionary<int, OrderLegalEntityAllocation>()
            : BuildAllocationMap(order, persistedAllocations);
        var preparedLines = new List<PreparedSalesReturnLine>();
        var handledLineIds = new HashSet<int>();

        // Classify every persisted return line independently. Historical or
        // transitional orders may contain both allocated and legacy lines.
        foreach (var salesReturnLine in salesReturnLines)
        {
            if (salesReturnLine.Id <= 0)
            {
                throw new InvalidOperationException(
                    "Sales return line must be persisted before LegalEntity planning.");
            }

            var orderLine = order.Lines.FirstOrDefault(x =>
                    x.Id == salesReturnLine.OrderLineId && !x.IsDeleted)
                ?? throw new InvalidOperationException(
                    $"Không tìm thấy OrderLine #{salesReturnLine.OrderLineId} của return.");
            var lineAllocations = persistedAllocations
                .Where(x => x.OrderLineId == orderLine.Id)
                .ToList();

            if (lineAllocations.Count == 0)
            {
                var fragmentsWithoutAllocation = await ReadFragmentsAsync(
                        order.Id,
                        salesReturnLine.OrderLineId,
                        salesReturnLine.Action == SalesReturnLineAction.Restock,
                        ct);
                if (fragmentsWithoutAllocation.Any(x =>
                        allocationByTransaction.ContainsKey(
                            x.InventoryTransactionId) ||
                        IsLegalEntitySource(x)))
                {
                    throw PartialLegalEntityEvidence(salesReturnLine);
                }

                continue;
            }

            var fragments = await ReadFragmentsAsync(
                order.Id,
                salesReturnLine.OrderLineId,
                salesReturnLine.Action == SalesReturnLineAction.Restock,
                ct);
            var lineAllocationByTransaction = lineAllocations.ToDictionary(
                x => x.InventoryTransactionId
                    ?? throw PartialLegalEntityEvidence(salesReturnLine),
                x => x);
            if (fragments.Count == 0 ||
                fragments.Any(x =>
                    !lineAllocationByTransaction.ContainsKey(
                        x.InventoryTransactionId)))
            {
                throw PartialLegalEntityEvidence(salesReturnLine);
            }

            var costAllocations = _returnCostAllocator.Allocate(
                fragments,
                salesReturnLine.ReturnBaseQuantity);
            List<ReversalPlan> plans;
            try
            {
                plans = BuildPlans(
                    orderLine,
                    fragments,
                    costAllocations,
                    lineAllocationByTransaction);
            }
            catch (InvalidOperationException exception)
            {
                throw PartialLegalEntityEvidence(
                    salesReturnLine,
                    exception);
            }

            AllocateFinancialAmounts(plans, salesReturnLine.RefundLineTotal);

            var isRestock =
                salesReturnLine.Action == SalesReturnLineAction.Restock;
            preparedLines.Add(new PreparedSalesReturnLine
            {
                SalesReturnLine = salesReturnLine,
                Plans = plans,
                IsRestock = isRestock,
                ReversalType = isRestock
                    ? OrderLegalEntityReversalType.ReturnRestock
                    : OrderLegalEntityReversalType.ReturnNoRestock,
                OccurredAtUtc = DateTime.UtcNow
            });
            handledLineIds.Add(salesReturnLine.Id);
        }

        var lockKeys = preparedLines
            .Where(x => x.IsRestock)
            .SelectMany(x => x.Plans)
            .Select(x => new InventoryPostingLockKey(
                order.StoreId,
                x.Source.WarehouseId,
                x.Source.ProductVariantId))
            .Distinct()
            .ToList();

        return new PreparedSalesReturnBatch
        {
            Order = order,
            SalesReturn = salesReturn,
            Lines = preparedLines,
            LockKeys = lockKeys,
            HandledLineIds = handledLineIds
        };
    }

    internal async Task ApplySalesReturnBatchAsync(
        PreparedSalesReturnBatch batch,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        foreach (var preparedLine in batch.Lines)
        {
            await ApplyPreparedSalesReturnLineCoreAsync(
                batch,
                preparedLine,
                ct);
        }
    }

    internal async Task ApplyPreparedSalesReturnLineAsync(
        PreparedSalesReturnBatch batch,
        int salesReturnLineId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var preparedLine = batch.Lines.SingleOrDefault(x =>
            x.SalesReturnLine.Id == salesReturnLineId);
        if (preparedLine is null)
        {
            throw new InvalidOperationException(
                $"SalesReturnLine #{salesReturnLineId} was not prepared as LegalEntity.");
        }

        await ApplyPreparedSalesReturnLineCoreAsync(
            batch,
            preparedLine,
            ct);
    }

    private async Task ApplyPreparedSalesReturnLineCoreAsync(
        PreparedSalesReturnBatch batch,
        PreparedSalesReturnLine preparedLine,
        CancellationToken ct)
    {
        var salesReturnLine = preparedLine.SalesReturnLine;
        var freshFragments = await ReadFragmentsAsync(batch.Order.Id,
            salesReturnLine.OrderLineId, preparedLine.IsRestock, ct);
        var freshAllocations = _returnCostAllocator.Allocate(freshFragments, salesReturnLine.ReturnBaseQuantity);
        var originalMap = BuildAllocationMap(batch.Order,
            await _allocations.GetForOrderAsync(batch.Order.Id, ct));
        var freshPlans = BuildPlans(preparedLine.Plans[0].OrderLine,
            freshFragments, freshAllocations, originalMap);
        EnsureSamePlanIdentity(preparedLine.Plans, freshPlans);
        AllocateFinancialAmounts(freshPlans, salesReturnLine.RefundLineTotal);
        preparedLine.Plans.Clear();
        preparedLine.Plans.AddRange(freshPlans);
        if (preparedLine.IsRestock)
        {
            salesReturnLine.LineCostTotal = preparedLine.Plans
                .Sum(x => x.BaseQuantity * x.UnitCost);
            salesReturnLine.UnitCostSnapshot =
                salesReturnLine.ReturnBaseQuantity > 0
                    ? Math.Round(
                        salesReturnLine.LineCostTotal /
                        salesReturnLine.ReturnBaseQuantity,
                        6,
                        MidpointRounding.AwayFromZero)
                    : 0m;
            salesReturnLine.IsProvisionalCost = preparedLine.Plans
                .Any(x => x.Source.IsProvisional);
        }

        var reversalRows = new List<OrderLegalEntityAllocationReversal>();
        foreach (var plan in preparedLine.Plans)
        {
            int? inventoryTransactionId = null;

            if (preparedLine.IsRestock)
            {
                var subKey =
                    $"LE-RET:R{salesReturnLine.Id}:A{plan.Allocation.Id}:S{plan.Source.SourceValuationEntryId}";
                var request = _movementFactory.CreateSaleRefund(
                    plan.Source.WarehouseId,
                    plan.Source.ProductVariantId,
                    batch.SalesReturn.Id,
                    salesReturnLine.Id,
                    plan.BaseQuantity,
                    plan.UnitCost,
                    batch.SalesReturn.Reason,
                    preparedLine.OccurredAtUtc,
                    subKey,
                    plan.Source.SourceValuationEntryId,
                    plan.Source.ReferenceSubKey);

                var movement = await _movements.CreateAsync(request, ct);
                EnsureMovementCreated(
                    movement,
                    "return",
                    batch.Order.Id,
                    plan.Source.SourceValuationEntryId);
                inventoryTransactionId = movement.InventoryTransactionId;
            }

            reversalRows.Add(BuildReversal(
                batch.Order,
                plan,
                preparedLine.ReversalType,
                plan.FinancialAmount,
                inventoryTransactionId,
                batch.SalesReturn,
                salesReturnLine,
                batch.SalesReturn.Reason,
                preparedLine.OccurredAtUtc));
        }

        await _reversals.AddRangeAsync(reversalRows, ct);
    }

    private static Dictionary<int, OrderLegalEntityAllocation> BuildAllocationMap(
        Order order,
        IReadOnlyCollection<OrderLegalEntityAllocation> allocations)
    {
        if (allocations.Any(x => x.StoreId != order.StoreId || x.OrderId != order.Id))
            throw new InvalidOperationException("Allocation reversal bị lẫn Store/Order.");

        if (allocations.Any(x => !x.InventoryTransactionId.HasValue))
            throw new InvalidOperationException(
                "Allocation gốc thiếu InventoryTransactionId nên không thể đảo an toàn.");

        return allocations.ToDictionary(
            x => x.InventoryTransactionId!.Value,
            x => x);
    }

    private Task<List<ReturnableValuationFragmentDto>> ReadFragmentsAsync(
        int orderId, int orderLineId, bool requireCost, CancellationToken ct)
        => !requireCost && _returnableFragments is IReturnableValuationCostEvidence evidence
            ? evidence.GetQuantityOnlyForOrderLineAsync(orderId, orderLineId, ct)
            : _returnableFragments.GetForOrderLineAsync(orderId, orderLineId, ct);

    private static void EnsureSamePlanIdentity(
        IReadOnlyList<ReversalPlan> original, IReadOnlyList<ReversalPlan> current)
    {
        if (!original.Select(x => (x.OrderLine.Id, x.Allocation.Id, x.Source.SourceValuationEntryId,
                    x.Source.WarehouseId, x.Source.ProductVariantId, x.BaseQuantity))
                .SequenceEqual(current.Select(x => (x.OrderLine.Id, x.Allocation.Id, x.Source.SourceValuationEntryId,
                    x.Source.WarehouseId, x.Source.ProductVariantId, x.BaseQuantity))))
            throw new InvalidOperationException("Return/void source plan changed after locking; retry the operation.");
    }

    private static List<ReversalPlan> BuildPlans(
        OrderLine orderLine,
        IReadOnlyCollection<ReturnableValuationFragmentDto> fragments,
        IReadOnlyCollection<ReturnCostAllocationDto> costAllocations,
        IReadOnlyDictionary<int, OrderLegalEntityAllocation> allocationByTransaction)
    {
        var fragmentById = fragments.ToDictionary(x => x.SourceValuationEntryId);
        var plans = new List<ReversalPlan>();

        foreach (var cost in costAllocations)
        {
            if (!fragmentById.TryGetValue(cost.SourceValuationEntryId, out var source))
                throw new InvalidOperationException(
                    $"Không tìm thấy source valuation #{cost.SourceValuationEntryId}.");

            if (!allocationByTransaction.TryGetValue(
                    source.InventoryTransactionId,
                    out var allocation))
            {
                throw new InvalidOperationException(
                    $"Source valuation #{source.SourceValuationEntryId} không thuộc allocation gốc của order.");
            }

            if (allocation.OrderLineId != orderLine.Id ||
                allocation.WarehouseId != source.WarehouseId ||
                allocation.ProductVariantId != source.ProductVariantId)
            {
                throw new InvalidOperationException(
                    $"Source valuation #{source.SourceValuationEntryId} không khớp allocation #{allocation.Id}.");
            }

            plans.Add(new ReversalPlan
            {
                OrderLine = orderLine,
                Allocation = allocation,
                Source = source,
                BaseQuantity = cost.Quantity,
                UnitCost = cost.UnitCost
            });
        }

        return plans;
    }

    private static void AllocateVoidFinancialAmounts(List<ReversalPlan> plans)
    {
        foreach (var group in plans.GroupBy(x => x.Allocation.Id))
        {
            var allocation = group.First().Allocation;
            var items = group.ToList();
            var quantity = items.Sum(x => x.BaseQuantity);
            if (Math.Abs(quantity - allocation.BaseQuantity) > QuantityTolerance)
            {
                throw new InvalidOperationException(
                    $"Void allocation #{allocation.Id} không đủ quantity gốc. " +
                    $"Expected={allocation.BaseQuantity}, Actual={quantity}.");
            }

            AllocateFinancialAmounts(items, allocation.NetAmount);
        }
    }

    private static void AllocateFinancialAmounts(
        IReadOnlyList<ReversalPlan> plans,
        decimal totalAmount)
    {
        if (plans.Count == 0)
            throw new InvalidOperationException("Không có reversal plan để phân bổ tiền.");

        var totalQuantity = plans.Sum(x => x.BaseQuantity);
        if (totalQuantity <= 0)
            throw new InvalidOperationException("Tổng quantity reversal không hợp lệ.");

        decimal allocated = 0m;
        for (var i = 0; i < plans.Count; i++)
        {
            var amount = i == plans.Count - 1
                ? totalAmount - allocated
                : Math.Round(
                    totalAmount * plans[i].BaseQuantity / totalQuantity,
                    2,
                    MidpointRounding.AwayFromZero);
            plans[i].FinancialAmount = amount;
            allocated += amount;
        }
    }

    private static OrderLegalEntityAllocationReversal BuildReversal(
        Order order,
        ReversalPlan plan,
        OrderLegalEntityReversalType reversalType,
        decimal financialAmount,
        int? inventoryTransactionId,
        SalesReturn? salesReturn,
        SalesReturnLine? salesReturnLine,
        string reason,
        DateTime occurredAtUtc)
        => new()
        {
            StoreId = order.StoreId,
            OrderId = order.Id,
            OrderLineId = plan.OrderLine.Id,
            OrderLegalEntityAllocationId = plan.Allocation.Id,
            SalesReturnId = salesReturn?.Id,
            SalesReturnLineId = salesReturnLine?.Id,
            LegalEntityId = plan.Allocation.LegalEntityId,
            WarehouseId = plan.Allocation.WarehouseId,
            ProductVariantId = plan.Allocation.ProductVariantId,
            SourceValuationEntryId = plan.Source.SourceValuationEntryId,
            InventoryTransactionId = inventoryTransactionId,
            ReversalType = reversalType,
            BaseQuantity = plan.BaseQuantity,
            FinancialAmount = financialAmount,
            OccurredAtUtc = occurredAtUtc,
            Note = Trim(reason, 500)
        };

    private static void EnsureMovementCreated(
        InventoryMovementResultDto movement,
        string operation,
        int orderId,
        int sourceValuationEntryId)
    {
        if (!movement.IsCreated || !movement.InventoryTransactionId.HasValue)
        {
            throw new InvalidOperationException(
                $"Movement {operation} bị trùng/không được tạo cho Order #{orderId}, " +
                $"source valuation #{sourceValuationEntryId}.");
        }
    }

    private static string? Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var clean = value.Trim();
        return clean.Length <= maxLength ? clean : clean[..maxLength];
    }

    private static bool IsLegalEntitySource(
        ReturnableValuationFragmentDto fragment)
        => fragment.ReferenceSubKey?.StartsWith(
               "LE:",
               StringComparison.Ordinal) == true;

    private static InvalidOperationException PartialLegalEntityEvidence(
        SalesReturnLine line,
        Exception? innerException = null)
        => new(
            $"SalesReturnLine #{line.Id} có persisted LegalEntity allocation/source evidence không đầy đủ hoặc không nhất quán.",
            innerException);

    internal sealed class PreparedSalesReturnBatch
    {
        public required Order Order { get; init; }
        public required SalesReturn SalesReturn { get; init; }
        public required IReadOnlyList<PreparedSalesReturnLine> Lines { get; init; }
        public required IReadOnlyList<InventoryPostingLockKey> LockKeys { get; init; }
        public required IReadOnlySet<int> HandledLineIds { get; init; }
    }

    internal sealed class PreparedSalesReturnLine
    {
        public required SalesReturnLine SalesReturnLine { get; init; }
        public required List<ReversalPlan> Plans { get; init; }
        public bool IsRestock { get; init; }
        public OrderLegalEntityReversalType ReversalType { get; init; }
        public DateTime OccurredAtUtc { get; init; }
    }

    internal sealed class ReversalPlan
    {
        public required OrderLine OrderLine { get; init; }
        public required OrderLegalEntityAllocation Allocation { get; init; }
        public required ReturnableValuationFragmentDto Source { get; init; }
        public decimal BaseQuantity { get; init; }
        public decimal UnitCost { get; init; }
        public decimal FinancialAmount { get; set; }
    }
}
