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
        fixture.Movements.Requests.Should().HaveCount(2);
        fixture.Movements.Requests[0].WarehouseId.Should().Be(22);
        fixture.Movements.Requests[0].QuantityChange.Should().Be(3m);
        fixture.Movements.Requests[1].WarehouseId.Should().Be(11);
        fixture.Movements.Requests[1].QuantityChange.Should().Be(1m);
        fixture.Reversals.Rows.Sum(x => x.FinancialAmount).Should().Be(400m);
        fixture.Reversals.Rows.Should().OnlyContain(x =>
            x.ReversalType == OrderLegalEntityReversalType.ReturnRestock);
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
        fixture.Movements.Requests.Should().BeEmpty();
        fixture.Reversals.Rows.Should().ContainSingle();
        fixture.Reversals.Rows[0].WarehouseId.Should().Be(22);
        fixture.Reversals.Rows[0].BaseQuantity.Should().Be(2m);
        fixture.Reversals.Rows[0].FinancialAmount.Should().Be(200m);
        fixture.Reversals.Rows[0].ReversalType.Should()
            .Be(OrderLegalEntityReversalType.ReturnNoRestock);
        fixture.Reversals.Rows[0].InventoryTransactionId.Should().BeNull();
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

    private sealed class FakeFragmentService : IReturnableValuationFragmentService
    {
        private readonly List<ReturnableValuationFragmentDto> _fragments;

        public FakeFragmentService(List<ReturnableValuationFragmentDto> fragments)
            => _fragments = fragments;

        public Task<List<ReturnableValuationFragmentDto>> GetForOrderLineAsync(
            int orderId,
            int orderLineId,
            CancellationToken ct = default)
            => Task.FromResult(_fragments);
    }

    private sealed class FakeMovementService : IInventoryMovementService
    {
        private int _nextId = 900;
        public List<CreateInventoryMovementRequest> Requests { get; } = [];

        public Task PreLockBalancesAsync(
            IEnumerable<InventoryPostingLockKey> keys,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<InventoryMovementResultDto> CreateAsync(
            CreateInventoryMovementRequest request,
            CancellationToken ct = default)
        {
            Requests.Add(request);
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
