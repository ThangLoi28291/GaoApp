using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Orders;

/// <summary>
/// Orchestrator reversal của order đã allocation. Không suy đoán lại SalePriority và
/// không dùng kho ca POS; mọi fragment phải quay về đúng allocation/valuation gốc.
/// Caller sở hữu transaction ngoài cùng.
/// </summary>
public sealed class OrderLegalEntityReversalService : IOrderLegalEntityReversalService
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

        var persistedAllocations = await _allocations.GetForOrderAsync(order.Id, ct);
        if (persistedAllocations.Count == 0)
            return false;

        var allocationByTransaction = BuildAllocationMap(order, persistedAllocations);
        var fragments = await _returnableFragments.GetForOrderLineAsync(
            order.Id,
            salesReturnLine.OrderLineId,
            ct);
        var costAllocations = _returnCostAllocator.Allocate(
            fragments,
            salesReturnLine.ReturnBaseQuantity);
        var orderLine = order.Lines.FirstOrDefault(x =>
                x.Id == salesReturnLine.OrderLineId && !x.IsDeleted)
            ?? throw new InvalidOperationException(
                $"Không tìm thấy OrderLine #{salesReturnLine.OrderLineId} của return.");
        var plans = BuildPlans(
            orderLine,
            fragments,
            costAllocations,
            allocationByTransaction);

        AllocateFinancialAmounts(plans, salesReturnLine.RefundLineTotal);

        var isRestock = salesReturnLine.Action == SalesReturnLineAction.Restock;
        var reversalType = isRestock
            ? OrderLegalEntityReversalType.ReturnRestock
            : OrderLegalEntityReversalType.ReturnNoRestock;
        var occurredAtUtc = DateTime.UtcNow;
        var reversalRows = new List<OrderLegalEntityAllocationReversal>();

        if (isRestock)
        {
            salesReturnLine.LineCostTotal = plans.Sum(x => x.BaseQuantity * x.UnitCost);
            salesReturnLine.UnitCostSnapshot = salesReturnLine.ReturnBaseQuantity > 0
                ? Math.Round(
                    salesReturnLine.LineCostTotal / salesReturnLine.ReturnBaseQuantity,
                    6,
                    MidpointRounding.AwayFromZero)
                : 0m;
            salesReturnLine.IsProvisionalCost = plans.Any(x => x.Source.IsProvisional);
        }

        foreach (var plan in plans)
        {
            int? inventoryTransactionId = null;

            if (isRestock)
            {
                var subKey =
                    $"LE-RET:R{salesReturnLine.Id}:A{plan.Allocation.Id}:S{plan.Source.SourceValuationEntryId}";
                var request = _movementFactory.CreateSaleRefund(
                    plan.Source.WarehouseId,
                    plan.Source.ProductVariantId,
                    salesReturn.Id,
                    salesReturnLine.Id,
                    plan.BaseQuantity,
                    plan.UnitCost,
                    salesReturn.Reason,
                    occurredAtUtc,
                    subKey,
                    plan.Source.SourceValuationEntryId,
                    plan.Source.ReferenceSubKey);

                var movement = await _movements.CreateAsync(request, ct);
                EnsureMovementCreated(
                    movement,
                    "return",
                    order.Id,
                    plan.Source.SourceValuationEntryId);
                inventoryTransactionId = movement.InventoryTransactionId;
            }

            reversalRows.Add(BuildReversal(
                order,
                plan,
                reversalType,
                plan.FinancialAmount,
                inventoryTransactionId,
                salesReturn,
                salesReturnLine,
                salesReturn.Reason,
                occurredAtUtc));
        }

        await _reversals.AddRangeAsync(reversalRows, ct);
        return true;
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

    private sealed class ReversalPlan
    {
        public required OrderLine OrderLine { get; init; }
        public required OrderLegalEntityAllocation Allocation { get; init; }
        public required ReturnableValuationFragmentDto Source { get; init; }
        public decimal BaseQuantity { get; init; }
        public decimal UnitCost { get; init; }
        public decimal FinancialAmount { get; set; }
    }
}
