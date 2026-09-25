using FluentAssertions;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Services.Inventory;
using GaoApp.Application.Services.Orders;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Inventory;

public sealed class OrderLegalEntityReversalServiceTests
{
    [Fact]
    public async Task Void_ShouldRestoreEveryOriginalWarehouseAndAllocationAmount()
    {
        var fixture = Fixture();

        var handled = await fixture.Service.ReverseVoidIfAllocatedAsync(
            fixture.Order,
            "UAT void split");

        handled.Should().BeTrue();
        fixture.Movements.PreLockBatches.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(
            [
                new InventoryPostingLockKey(1, 11, 99),
                new InventoryPostingLockKey(1, 22, 99)
            ]);
        fixture.Movements.Events.First().Should().Be("prelock");
        fixture.Movements.Requests.Should().HaveCount(2);
        fixture.Movements.Requests.Should().ContainSingle(x =>
            x.WarehouseId == 11 && x.QuantityChange == 5m && x.SourceValuationEntryId == 1001);
        fixture.Movements.Requests.Should().ContainSingle(x =>
            x.WarehouseId == 22 && x.QuantityChange == 3m && x.SourceValuationEntryId == 1002);
        fixture.Reversals.Rows.Sum(x => x.BaseQuantity).Should().Be(8m);
        fixture.Reversals.Rows.Sum(x => x.FinancialAmount).Should().Be(800m);
        fixture.Reversals.Rows.Single(x => x.WarehouseId == 11).FinancialAmount.Should().Be(500m);
        fixture.Reversals.Rows.Single(x => x.WarehouseId == 22).FinancialAmount.Should().Be(300m);
        fixture.Reversals.Rows.Should().OnlyContain(x =>
            x.ReversalType == OrderLegalEntityReversalType.Void &&
            x.InventoryTransactionId.HasValue);
        fixture.Movements.Requests.Select(x => x.ReferenceSubKey)
            .Should().Equal(
                "LE-VOID:A2:S1002",
                "LE-VOID:A1:S1001");
    }

    [Fact]
    public async Task PartialReturn_ShouldReverseNewestSourceFirstAcrossLegalEntities()
    {
        var fixture = Fixture();
        var salesReturn = Return();
        var line = ReturnLine(SalesReturnLineAction.Restock, 4m, 400m);

        var handled = await fixture.Service.ReverseSalesReturnLineIfAllocatedAsync(
            fixture.Order,
            salesReturn,
            line);

        handled.Should().BeTrue();
        fixture.Movements.PreLockBatches.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(
            [
                new InventoryPostingLockKey(1, 11, 99),
                new InventoryPostingLockKey(1, 22, 99)
            ]);
        fixture.Movements.Events.First().Should().Be("prelock");
        fixture.Movements.Requests.Should().HaveCount(2);
        fixture.Movements.Requests[0].WarehouseId.Should().Be(22);
        fixture.Movements.Requests[0].QuantityChange.Should().Be(3m);
        fixture.Movements.Requests[1].WarehouseId.Should().Be(11);
        fixture.Movements.Requests[1].QuantityChange.Should().Be(1m);
        fixture.Reversals.Rows.Sum(x => x.FinancialAmount).Should().Be(400m);
        fixture.Reversals.Rows.Should().OnlyContain(x =>
            x.ReversalType == OrderLegalEntityReversalType.ReturnRestock);
        fixture.Movements.Requests.Select(x => x.ReferenceSubKey)
            .Should().Equal(
                "LE-RET:R300:A2:S1002",
                "LE-RET:R300:A1:S1001");
        line.LineCostTotal.Should().Be(46m);
        line.UnitCostSnapshot.Should().Be(11.5m);
    }

    [Fact]
    public async Task NoRestock_ShouldConsumeSourceInLedgerWithoutInventoryMovement()
    {
        var fixture = Fixture();
        var salesReturn = Return();
        var line = ReturnLine(SalesReturnLineAction.NoRestock, 2m, 200m);

        var handled = await fixture.Service.ReverseSalesReturnLineIfAllocatedAsync(
            fixture.Order,
            salesReturn,
            line);

        handled.Should().BeTrue();
        fixture.Movements.PreLockBatches.Should().BeEmpty();
        fixture.Movements.Requests.Should().BeEmpty();
        fixture.Reversals.Rows.Should().ContainSingle();
        fixture.Reversals.Rows[0].WarehouseId.Should().Be(22);
        fixture.Reversals.Rows[0].BaseQuantity.Should().Be(2m);
        fixture.Reversals.Rows[0].FinancialAmount.Should().Be(200m);
        fixture.Reversals.Rows[0].ReversalType.Should()
            .Be(OrderLegalEntityReversalType.ReturnNoRestock);
        fixture.Reversals.Rows[0].InventoryTransactionId.Should().BeNull();
    }

    [Fact]
    public async Task RestockDirectCaller_WithoutActiveTransaction_ShouldRejectBeforeMovement()
    {
        var fixture = Fixture();
        fixture.Movements.HasActiveTransaction = false;

        var action = () => fixture.Service.ReverseSalesReturnLineIfAllocatedAsync(
            fixture.Order,
            Return(),
            ReturnLine(SalesReturnLineAction.Restock, 2m, 200m));

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*active transaction*");
        fixture.Movements.Requests.Should().BeEmpty();
        fixture.Reversals.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task BatchPreparation_ShouldHandleOnlyAllocatedLineAndLeaveLegacyLineUnhandled()
    {
        var fixture = MixedFixture();
        var salesReturn = Return();
        var allocatedLine = ReturnLine(
            SalesReturnLineAction.Restock,
            2m,
            200m);
        var legacyLine = ReturnLine(
            SalesReturnLineAction.Restock,
            1m,
            100m);
        legacyLine.Id = 301;
        legacyLine.OrderLineId = 20;
        legacyLine.VariantId = 199;

        var batch = await PrepareBatchAsync(
            fixture.Service,
            fixture.Order,
            salesReturn,
            [allocatedLine, legacyLine]);

        BatchHandledLineIds(batch).Should().Equal(allocatedLine.Id);
        BatchLockKeys(batch).Should().Equal(
            new InventoryPostingLockKey(1, 11, 99));
        fixture.Movements.PreLockBatches.Should().BeEmpty();
        fixture.Movements.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task BatchPreparation_PartialEvidence_ShouldFailClosedBeforePrelockOrMovement()
    {
        var fixture = PartialEvidenceFixture();
        var salesReturn = Return();
        var line = ReturnLine(
            SalesReturnLineAction.Restock,
            2m,
            200m);

        var action = () => PrepareBatchAsync(
            fixture.Service,
            fixture.Order,
            salesReturn,
            [line]);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*evidence*");
        fixture.Movements.PreLockBatches.Should().BeEmpty();
        fixture.Movements.Requests.Should().BeEmpty();
        fixture.Reversals.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task PreparedBatchApply_ShouldNotPrelock()
    {
        var fixture = Fixture();
        var salesReturn = Return();
        var line = ReturnLine(
            SalesReturnLineAction.Restock,
            2m,
            200m);
        var batch = await PrepareBatchAsync(
            fixture.Service,
            fixture.Order,
            salesReturn,
            [line]);

        await ApplyBatchAsync(fixture.Service, batch);

        fixture.Movements.PreLockBatches.Should().BeEmpty();
        fixture.Movements.Requests.Should().HaveCount(1);
        fixture.Reversals.Rows.Should().ContainSingle();
    }

    private static TestFixture Fixture()
    {
        var orderLine = new OrderLine
        {
            Id = 10,
            StoreId = 1,
            OrderId = 100,
            ProductId = 9,
            VariantId = 99,
            ItemName = "Phase 22.6 product",
            Quantity = 8m,
            Multiplier = 1m,
            BaseQuantity = 8m,
            LineTotal = 800m
        };
        var order = new Order
        {
            Id = 100,
            StoreId = 1,
            OrderNumber = "POS-226",
            Status = OrderStatus.Completed,
            GrandTotal = 800m,
            Lines = new List<OrderLine> { orderLine }
        };
        var allocations = new List<OrderLegalEntityAllocation>
        {
            Allocation(1, 501, 1, 11, 5m, 500m),
            Allocation(2, 502, 2, 22, 3m, 300m)
        };
        var repository = new FakeAllocationRepository(allocations);
        var reversals = new FakeReversalRepository();
        var fragments = new FakeFragmentService(new List<ReturnableValuationFragmentDto>
        {
            Fragment(1001, 501, 11, 5m, 10m, DateTime.UtcNow.AddMinutes(-2)),
            Fragment(1002, 502, 22, 3m, 12m, DateTime.UtcNow.AddMinutes(-1))
        });
        var movements = new FakeMovementService();
        var service = new OrderLegalEntityReversalService(
            repository,
            reversals,
            fragments,
            new ReturnCostAllocator(),
            movements,
            new InventoryMovementFactory());

        return new TestFixture(order, service, reversals, movements);
    }

    private static TestFixture MixedFixture()
    {
        var allocatedLine = new OrderLine
        {
            Id = 10,
            StoreId = 1,
            OrderId = 100,
            ProductId = 9,
            VariantId = 99,
            ItemName = "Allocated line",
            Quantity = 2m,
            Multiplier = 1m,
            BaseQuantity = 2m,
            LineTotal = 200m
        };
        var legacyLine = new OrderLine
        {
            Id = 20,
            StoreId = 1,
            OrderId = 100,
            ProductId = 19,
            VariantId = 199,
            ItemName = "Legacy line",
            Quantity = 1m,
            Multiplier = 1m,
            BaseQuantity = 1m,
            LineTotal = 100m
        };
        var order = new Order
        {
            Id = 100,
            StoreId = 1,
            OrderNumber = "POS-MIXED",
            Status = OrderStatus.Completed,
            GrandTotal = 300m,
            Lines = [allocatedLine, legacyLine]
        };
        var repository = new FakeAllocationRepository(
        [
            Allocation(1, 501, 1, 11, 2m, 200m)
        ]);
        var reversals = new FakeReversalRepository();
        var fragments = new FakeFragmentService(
            new Dictionary<int, List<ReturnableValuationFragmentDto>>
            {
                [10] =
                [
                    Fragment(1001, 501, 11, 2m, 10m, DateTime.UtcNow)
                ],
                [20] =
                [
                    new ReturnableValuationFragmentDto
                    {
                        SourceValuationEntryId = 2001,
                        InventoryTransactionId = 777,
                        WarehouseId = 22,
                        ProductVariantId = 199,
                        SourceQuantityAbs = 1m,
                        RemainingQuantityAbs = 1m,
                        UnitCost = 12m,
                        OccurredAtUtc = DateTime.UtcNow,
                        ReferenceSubKey = "LEGACY-SRC"
                    }
                ]
            });
        var movements = new FakeMovementService();
        var service = new OrderLegalEntityReversalService(
            repository,
            reversals,
            fragments,
            new ReturnCostAllocator(),
            movements,
            new InventoryMovementFactory());

        return new TestFixture(order, service, reversals, movements);
    }

    private static TestFixture PartialEvidenceFixture()
    {
        var fixture = Fixture();
        fixture.Order.Lines.Single().BaseQuantity = 2m;
        fixture.Order.Lines.Single().Quantity = 2m;
        var repository = new FakeAllocationRepository(
        [
            Allocation(1, 501, 1, 11, 2m, 200m)
        ]);
        var fragments = new FakeFragmentService(
        [
            Fragment(1001, 501, 11, 1m, 10m, DateTime.UtcNow),
            Fragment(1002, 999, 11, 1m, 10m, DateTime.UtcNow.AddSeconds(1))
        ]);
        var reversals = new FakeReversalRepository();
        var movements = new FakeMovementService();
        var service = new OrderLegalEntityReversalService(
            repository,
            reversals,
            fragments,
            new ReturnCostAllocator(),
            movements,
            new InventoryMovementFactory());
        return new TestFixture(
            fixture.Order,
            service,
            reversals,
            movements);
    }

    private static async Task<object> PrepareBatchAsync(
        OrderLegalEntityReversalService service,
        Order order,
        SalesReturn salesReturn,
        IReadOnlyCollection<SalesReturnLine> lines)
    {
        var method = typeof(OrderLegalEntityReversalService).GetMethod(
            "PrepareSalesReturnBatchAsync",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "Prepared batch method was not found.");
        var task = (Task?)method.Invoke(
            service,
            [order, salesReturn, lines, CancellationToken.None])
            ?? throw new InvalidOperationException(
                "Prepared batch method did not return a task.");
        await task;
        return task.GetType().GetProperty("Result")?.GetValue(task)
            ?? throw new InvalidOperationException(
                "Prepared batch result was null.");
    }

    private static IReadOnlySet<int> BatchHandledLineIds(object batch)
        => (IReadOnlySet<int>)(batch.GetType()
            .GetProperty("HandledLineIds")?
            .GetValue(batch)
            ?? throw new InvalidOperationException(
                "HandledLineIds were not exposed."));

    private static IReadOnlyList<InventoryPostingLockKey> BatchLockKeys(
        object batch)
        => (IReadOnlyList<InventoryPostingLockKey>)(batch.GetType()
            .GetProperty("LockKeys")?
            .GetValue(batch)
            ?? throw new InvalidOperationException(
                "LockKeys were not exposed."));

    private static async Task ApplyBatchAsync(
        OrderLegalEntityReversalService service,
        object batch)
    {
        var method = typeof(OrderLegalEntityReversalService).GetMethod(
            "ApplySalesReturnBatchAsync",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "Prepared apply method was not found.");
        var task = (Task?)method.Invoke(
            service,
            [batch, CancellationToken.None])
            ?? throw new InvalidOperationException(
                "Prepared apply method did not return a task.");
        await task;
    }

    private static OrderLegalEntityAllocation Allocation(
        int id,
        int inventoryTransactionId,
        int legalEntityId,
        int warehouseId,
        decimal quantity,
        decimal netAmount)
        => new()
        {
            Id = id,
            StoreId = 1,
            OrderId = 100,
            OrderLineId = 10,
            ProductVariantId = 99,
            LegalEntityId = legalEntityId,
            WarehouseId = warehouseId,
            InventoryTransactionId = inventoryTransactionId,
            SalePriority = legalEntityId,
            Quantity = quantity,
            BaseQuantity = quantity,
            NetAmount = netAmount
        };

    private static ReturnableValuationFragmentDto Fragment(
        int id,
        int inventoryTransactionId,
        int warehouseId,
        decimal quantity,
        decimal unitCost,
        DateTime occurredAtUtc)
        => new()
        {
            SourceValuationEntryId = id,
            InventoryTransactionId = inventoryTransactionId,
            WarehouseId = warehouseId,
            ProductVariantId = 99,
            SourceQuantityAbs = quantity,
            RemainingQuantityAbs = quantity,
            UnitCost = unitCost,
            OccurredAtUtc = occurredAtUtc,
            ReferenceSubKey = $"SRC-{id}"
        };

    private static SalesReturn Return()
        => new()
        {
            Id = 200,
            StoreId = 1,
            OrderId = 100,
            ReturnNumber = "RET-226",
            Reason = "UAT partial return",
            Status = SalesReturnStatus.Completed
        };

    private static SalesReturnLine ReturnLine(
        SalesReturnLineAction action,
        decimal quantity,
        decimal refundTotal)
        => new()
        {
            Id = 300,
            StoreId = 1,
            SalesReturnId = 200,
            OrderLineId = 10,
            ProductId = 9,
            VariantId = 99,
            ItemName = "Phase 22.6 product",
            ReturnQuantity = quantity,
            ReturnBaseQuantity = quantity,
            RefundLineTotal = refundTotal,
            Action = action
        };

    private sealed record TestFixture(
        Order Order,
        OrderLegalEntityReversalService Service,
        FakeReversalRepository Reversals,
        FakeMovementService Movements);

    private sealed class FakeAllocationRepository : IOrderLegalEntityAllocationRepository
    {
        private readonly List<OrderLegalEntityAllocation> _allocations;

        public FakeAllocationRepository(List<OrderLegalEntityAllocation> allocations)
            => _allocations = allocations;

        public Task<List<OrderLegalEntityAllocation>> GetForOrderAsync(
            int orderId,
            CancellationToken ct = default)
            => Task.FromResult(_allocations.Where(x => x.OrderId == orderId).ToList());

        public Task<bool> AnyForOrderAsync(int orderId, CancellationToken ct = default)
            => Task.FromResult(_allocations.Any(x => x.OrderId == orderId));

        public Task<Store?> GetStoreFeatureStateAsync(int storeId, CancellationToken ct = default)
            => Task.FromResult<Store?>(null);

        public Task<List<LegalEntity>> GetActiveSalesLegalEntitiesAsync(
            int storeId,
            CancellationToken ct = default)
            => Task.FromResult(new List<LegalEntity>());

        public Task<List<InventoryBalance>> LockInventoryForAllocationAsync(
            int storeId,
            IReadOnlyCollection<int> warehouseIds,
            IReadOnlyCollection<int> productVariantIds,
            CancellationToken ct = default)
            => Task.FromResult(new List<InventoryBalance>());

        public Task AddRangeAsync(
            IReadOnlyCollection<OrderLegalEntityAllocation> allocations,
            CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class FakeReversalRepository : IOrderLegalEntityAllocationReversalRepository
    {
        public List<OrderLegalEntityAllocationReversal> Rows { get; } = [];

        public Task<Dictionary<int, decimal>> GetReversedBaseQuantityBySourceEntryIdsAsync(
            IReadOnlyCollection<int> sourceValuationEntryIds,
            CancellationToken ct = default)
            => Task.FromResult(new Dictionary<int, decimal>());

        public Task<List<OrderLegalEntityAllocationReversal>> GetForOrderAsync(
            int orderId,
            CancellationToken ct = default)
            => Task.FromResult(Rows.Where(x => x.OrderId == orderId).ToList());

        public Task AddRangeAsync(
            IReadOnlyCollection<OrderLegalEntityAllocationReversal> reversals,
            CancellationToken ct = default)
        {
            Rows.AddRange(reversals);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class FakeFragmentService : IReturnableValuationFragmentService,
        IReturnableValuationCostEvidence
    {
        public Task<List<ReturnableValuationFragmentDto>> GetQuantityOnlyForOrderLineAsync(
            int orderId, int orderLineId, CancellationToken ct)
            => GetForOrderLineAsync(orderId, orderLineId, ct);

        public Task ValidateVoidClosureAsync(int orderId, int orderLineId, CancellationToken ct)
        {
            // This recording fixture checks orchestration only. Persisted closure
            // is exercised with the real reader in SaleCostReversalIntegrationTests.
            if (!_fragmentsByOrderLine.ContainsKey(orderLineId))
                throw new InvalidOperationException("Unknown closure line.");
            return Task.CompletedTask;
        }

        private readonly IReadOnlyDictionary<
            int,
            List<ReturnableValuationFragmentDto>> _fragmentsByOrderLine;

        public FakeFragmentService(List<ReturnableValuationFragmentDto> fragments)
            => _fragmentsByOrderLine =
                new Dictionary<int, List<ReturnableValuationFragmentDto>>
                {
                    [10] = fragments
                };

        public FakeFragmentService(
            IReadOnlyDictionary<
                int,
                List<ReturnableValuationFragmentDto>> fragmentsByOrderLine)
            => _fragmentsByOrderLine = fragmentsByOrderLine;

        public Task<List<ReturnableValuationFragmentDto>> GetForOrderLineAsync(
            int orderId,
            int orderLineId,
            CancellationToken ct = default)
            => Task.FromResult(
                _fragmentsByOrderLine.TryGetValue(
                    orderLineId,
                    out var fragments)
                    ? fragments
                    : new List<ReturnableValuationFragmentDto>());
    }

    private sealed class FakeMovementService : IInventoryMovementService
    {
        private int _nextId = 900;
        public List<CreateInventoryMovementRequest> Requests { get; } = [];
        public List<List<InventoryPostingLockKey>> PreLockBatches { get; } = [];
        public List<string> Events { get; } = [];
        public bool HasActiveTransaction { get; set; } = true;

        public Task PreLockBalancesAsync(
            IEnumerable<InventoryPostingLockKey> keys,
            CancellationToken ct = default)
        {
            var batch = keys.ToList();
            if (batch.Count > 0 && !HasActiveTransaction)
            {
                throw new InvalidOperationException(
                    "Inventory balance batch pre-locking requires an active transaction.");
            }

            PreLockBatches.Add(batch);
            Events.Add("prelock");
            return Task.CompletedTask;
        }

        public Task<InventoryMovementResultDto> CreateAsync(
            CreateInventoryMovementRequest request,
            CancellationToken ct = default)
        {
            Requests.Add(request);
            Events.Add($"movement:{request.WarehouseId}");
            return Task.FromResult(new InventoryMovementResultDto
            {
                IsCreated = true,
                InventoryTransactionId = _nextId++,
                WarehouseId = request.WarehouseId,
                ProductVariantId = request.ProductVariantId,
                QuantityChange = request.QuantityChange
            });
        }

        public Task<decimal> PeekOutboundUnitCostAsync(
            int warehouseId,
            int productVariantId,
            decimal quantity,
            CancellationToken ct = default)
            => Task.FromResult(0m);
    }
}
