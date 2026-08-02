using FluentAssertions;
using GaoApp.Application.Common.Exceptions.Pos;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Services.Inventory;
using GaoApp.Application.Services.Orders;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Inventory;

public sealed class OrderLegalEntityFinalizeServiceTests
{
    [Fact]
    public async Task ApplyIfEnabled_FlagOff_ShouldPreserveLegacyPathWithoutSideEffects()
    {
        var repository = Repository(featureEnabled: false);
        var movements = new FakeInventoryMovementService(repository);
        var service = CreateService(repository, movements);
        var order = Order(quantity: 8m, lineTotal: 800m, grandTotal: 800m);

        var result = await service.ApplyIfEnabledAsync(order);

        result.WasApplied.Should().BeFalse();
        result.IsFeatureEnabled.Should().BeFalse();
        repository.LockCallCount.Should().Be(0);
        repository.Added.Should().BeEmpty();
        movements.PreLockBatches.Should().BeEmpty();
        movements.Requests.Should().BeEmpty();
        order.LegalEntityCount.Should().Be(0);
        order.LegalEntityAllocatedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task ApplyIfEnabled_Hkd1HasEnough_ShouldIssueOnlyItsDefaultWarehouse()
    {
        var repository = Repository(
            featureEnabled: true,
            Balance(11, 101, 10m),
            Balance(22, 101, 20m));
        var movements = new FakeInventoryMovementService(repository);
        var service = CreateService(repository, movements);
        var order = Order(quantity: 8m, lineTotal: 800m, grandTotal: 800m);

        var result = await service.ApplyIfEnabledAsync(order);

        result.WasApplied.Should().BeTrue();
        result.LegalEntityCount.Should().Be(1);
        movements.Requests.Should().ContainSingle(x =>
            x.WarehouseId == 11 &&
            x.QuantityChange == -8m &&
            x.AllowNegativeBalance == true &&
            x.ReferenceSubKey == "LE:1:WH:11");
        movements.PreLockBatches.Should().ContainSingle()
            .Which.Should().Equal(
                new InventoryPostingLockKey(7, 11, 101));
        repository.Added.Should().ContainSingle(x =>
            x.LegalEntityId == 1 &&
            x.WarehouseId == 11 &&
            x.BaseQuantity == 8m &&
            x.NetAmount == 800m &&
            x.InventoryTransactionId.HasValue);
        order.LegalEntityCount.Should().Be(1);
        order.HasMultipleLegalEntities.Should().BeFalse();
        order.LegalEntityAllocatedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task ApplyIfEnabled_SplitOrder_ShouldPersistExactFinancialAllocationAndUnifiedOrderTotal()
    {
        var repository = Repository(
            featureEnabled: true,
            Balance(11, 101, 5m),
            Balance(22, 101, 10m));
        var movements = new FakeInventoryMovementService(repository);
        var service = CreateService(repository, movements);
        var order = Order(
            quantity: 8m,
            lineTotal: 680m,
            grandTotal: 580m,
            lineDiscount: 80m,
            promotionDiscount: 40m,
            comboDiscount: 20m,
            orderDiscount: 30m,
            voucherDiscount: 50m);

        var result = await service.ApplyIfEnabledAsync(order);

        result.LegalEntityCount.Should().Be(2);
        result.AllocationCount.Should().Be(2);
        order.HasMultipleLegalEntities.Should().BeTrue();
        order.GrandTotal.Should().Be(580m);
        order.PaidTotal.Should().Be(580m);
        movements.Requests.Select(x => (x.WarehouseId, x.QuantityChange))
            .Should().Equal((11, -5m), (22, -3m));
        repository.Added.Select(x => (x.LegalEntityId, x.BaseQuantity))
            .Should().Equal((1, 5m), (2, 3m));
        repository.Added.Sum(x => x.LineTotal).Should().Be(680m);
        repository.Added.Sum(x => x.DiscountAllocated).Should().Be(80m);
        repository.Added.Sum(x => x.PromotionDiscountAllocated).Should().Be(40m);
        repository.Added.Sum(x => x.ComboDiscountAllocated).Should().Be(20m);
        repository.Added.Sum(x => x.OrderDiscountAllocated).Should().Be(30m);
        repository.Added.Sum(x => x.VoucherDiscountAllocated).Should().Be(50m);
        repository.Added.Sum(x => x.NetAmount).Should().Be(order.GrandTotal);
        repository.Added.Should().OnlyContain(x => x.InventoryTransactionId.HasValue);
        order.Lines.Single().LineCostTotal.Should().Be(110m);
        order.Lines.Single().UnitCostSnapshot.Should().Be(13.75m);
        movements.PreLockBatches.Should().ContainSingle()
            .Which.Should().Equal(
                new InventoryPostingLockKey(7, 11, 101),
                new InventoryPostingLockKey(7, 22, 101));
        repository.Events.Should().Equal(
            "lock",
            "prelock",
            "movement:11",
            "movement:22",
            "add");
    }

    [Fact]
    public async Task ApplyIfEnabled_InsufficientAcrossAllHkd_ShouldFinalizeNegativeAndCreateIssueResult()
    {
        var repository = Repository(
            featureEnabled: true,
            Balance(11, 101, 2m),
            Balance(22, 101, 3m));
        var movements = new FakeInventoryMovementService(repository);
        var service = CreateService(repository, movements);
        var order = Order(quantity: 8m, lineTotal: 800m, grandTotal: 800m);

        var result = await service.ApplyIfEnabledAsync(order);

        result.WasApplied.Should().BeTrue();
        result.HasNegativeInventory.Should().BeTrue();
        result.IssueLines.Should().ContainSingle(x =>
            x.OrderLineId == 501 &&
            x.IsNegativeInventory &&
            x.BeforeQty == 0m &&
            x.AfterQty == -3m);
        repository.LockCallCount.Should().Be(1);
        movements.Requests.Select(x => (x.WarehouseId, x.QuantityChange, x.AllowNegativeBalance))
            .Should().Equal((11, -2m, true), (22, -6m, true));
        repository.Added.Select(x => (x.LegalEntityId, x.BaseQuantity, x.AllocationSource))
            .Should().Equal(
                (1, 2m, OrderLegalEntityAllocationSource.AutoBySalePriority),
                (2, 6m, OrderLegalEntityAllocationSource.AutoNegativeFallback));
        order.LegalEntityCount.Should().Be(2);
        order.LegalEntityAllocatedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task ApplyIfEnabled_InvalidActivation_ShouldBlockBeforeLockAndMovement()
    {
        var repository = Repository(featureEnabled: true, Balance(11, 101, 10m));
        repository.Store.MultiLegalEntityActivatedAtUtc = null;
        var movements = new FakeInventoryMovementService(repository);
        var service = CreateService(repository, movements);

        var action = () => service.ApplyIfEnabledAsync(
            Order(quantity: 1m, lineTotal: 100m, grandTotal: 100m));

        var exception = await action.Should().ThrowAsync<PosAppException>();
        exception.Which.ErrorCode.Should().Be(PosErrorCodes.CheckoutLegalEntityConfigurationInvalid);
        repository.LockCallCount.Should().Be(0);
        movements.Requests.Should().BeEmpty();
        repository.Added.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyIfEnabled_MovementConflict_ShouldNotAttachAllocationRows()
    {
        var repository = Repository(featureEnabled: true, Balance(11, 101, 10m));
        var movements = new FakeInventoryMovementService(repository) { ReturnConflict = true };
        var service = CreateService(repository, movements);
        var order = Order(quantity: 2m, lineTotal: 200m, grandTotal: 200m);

        var action = () => service.ApplyIfEnabledAsync(order);

        var exception = await action.Should().ThrowAsync<PosAppException>();
        exception.Which.ErrorCode.Should().Be(PosErrorCodes.CheckoutLegalEntityMovementConflict);
        movements.Requests.Should().ContainSingle();
        repository.Added.Should().BeEmpty();
        order.LegalEntityCount.Should().Be(0);
        order.LegalEntityAllocatedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task CaptureMode_FeatureOn_ShouldPersistMultiCohortAndActivationSnapshot()
    {
        var repository = Repository(featureEnabled: true);
        var service = CreateService(repository, new FakeInventoryMovementService(repository));
        var order = Order(quantity: 1m, lineTotal: 100m, grandTotal: 100m);

        await service.CaptureModeAsync(order);

        order.UseMultiLegalEntity.Should().BeTrue();
        order.LegalEntityModeCapturedAtUtc.Should().NotBeNull();
        order.LegalEntityActivationAtUtcSnapshot.Should()
            .Be(repository.Store.MultiLegalEntityActivatedAtUtc);
    }

    [Fact]
    public async Task ApplyIfEnabled_CapturedMultiThenKillSwitch_ShouldKeepMultiCohort()
    {
        var repository = Repository(
            featureEnabled: true,
            Balance(11, 101, 10m),
            Balance(22, 101, 10m));
        var movements = new FakeInventoryMovementService(repository);
        var service = CreateService(repository, movements);
        var order = Order(quantity: 2m, lineTotal: 200m, grandTotal: 200m);
        await service.CaptureModeAsync(order);

        repository.Store.IsMultiLegalEntityEnabled = false;
        repository.Store.MultiLegalEntityActivatedAtUtc = null;
        var result = await service.ApplyIfEnabledAsync(order);

        result.WasApplied.Should().BeTrue();
        movements.Requests.Should().ContainSingle(x => x.WarehouseId == 11);
    }

    [Fact]
    public async Task ApplyIfEnabled_CapturedLegacyThenCanaryEnabled_ShouldStayLegacy()
    {
        var repository = Repository(featureEnabled: false, Balance(11, 101, 10m));
        var movements = new FakeInventoryMovementService(repository);
        var service = CreateService(repository, movements);
        var order = Order(quantity: 2m, lineTotal: 200m, grandTotal: 200m);
        await service.CaptureModeAsync(order);

        repository.Store.IsMultiLegalEntityEnabled = true;
        repository.Store.MultiLegalEntityActivatedAtUtc = DateTime.UtcNow;
        var result = await service.ApplyIfEnabledAsync(order);

        result.WasApplied.Should().BeFalse();
        result.IsFeatureEnabled.Should().BeFalse();
        repository.LockCallCount.Should().Be(0);
        movements.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyIfEnabled_PrePhase229OrderOlderThanActivation_ShouldStayLegacy()
    {
        var repository = Repository(featureEnabled: true, Balance(11, 101, 10m));
        repository.Store.MultiLegalEntityActivatedAtUtc = DateTime.UtcNow.AddMinutes(-5);
        var movements = new FakeInventoryMovementService(repository);
        var service = CreateService(repository, movements);
        var order = Order(quantity: 1m, lineTotal: 100m, grandTotal: 100m);
        order.CreatedAtUtc = DateTime.UtcNow.AddHours(-1);

        var result = await service.ApplyIfEnabledAsync(order);

        result.WasApplied.Should().BeFalse();
        repository.LockCallCount.Should().Be(0);
    }

    private static OrderLegalEntityFinalizeService CreateService(
        FakeAllocationRepository repository,
        FakeInventoryMovementService movements)
        => new(
            repository,
            new OrderLegalEntityAllocationService(),
            movements,
            new InventoryMovementFactory());

    private static FakeAllocationRepository Repository(
        bool featureEnabled,
        params InventoryBalance[] balances)
        => new()
        {
            Store = new Store
            {
                Id = 7,
                IsMultiLegalEntityEnabled = featureEnabled,
                MultiLegalEntityActivatedAtUtc = DateTime.UtcNow.AddDays(-1)
            },
            LegalEntities =
            [
                LegalEntity(1, 11, 1),
                LegalEntity(2, 22, 2)
            ],
            Balances = balances.ToList()
        };

    private static LegalEntity LegalEntity(int id, int warehouseId, int priority)
    {
        var warehouse = new Warehouse
        {
            Id = warehouseId,
            StoreId = 7,
            LegalEntityId = id,
            Code = $"WH{id}",
            Name = $"Kho HKD {id}",
            IsActive = true
        };

        return new LegalEntity
        {
            Id = id,
            StoreId = 7,
            Code = $"HKD{id}",
            Name = $"HKD {id}",
            LegalName = $"Hộ kinh doanh {id}",
            SalePriority = priority,
            IsActive = true,
            DefaultWarehouseId = warehouseId,
            DefaultWarehouse = warehouse
        };
    }

    private static InventoryBalance Balance(
        int warehouseId,
        int variantId,
        decimal onHand,
        decimal reserved = 0m)
        => new()
        {
            Id = (warehouseId * 1000) + variantId,
            StoreId = 7,
            WarehouseId = warehouseId,
            ProductVariantId = variantId,
            OnHandQty = onHand,
            ReservedQty = reserved
        };

    private static Order Order(
        decimal quantity,
        decimal lineTotal,
        decimal grandTotal,
        decimal lineDiscount = 0m,
        decimal promotionDiscount = 0m,
        decimal comboDiscount = 0m,
        decimal orderDiscount = 0m,
        decimal voucherDiscount = 0m)
    {
        var line = new OrderLine
        {
            Id = 501,
            StoreId = 7,
            OrderId = 1001,
            ProductId = 91,
            VariantId = 101,
            ItemName = "Sản phẩm test",
            Quantity = quantity,
            Multiplier = 1m,
            BaseQuantity = quantity,
            UnitPrice = 100m,
            OriginalUnitPrice = 100m,
            LineDiscount = lineDiscount,
            PromotionDiscount = promotionDiscount,
            LineTotal = lineTotal,
            Variant = new ProductVariant
            {
                Id = 101,
                StoreId = 7,
                ProductId = 91,
                Sku = "TEST-101",
                CostPrice = 10m
            }
        };

        return new Order
        {
            Id = 1001,
            StoreId = 7,
            Subtotal = quantity * 100m,
            DiscountTotal = lineDiscount + promotionDiscount,
            ComboDiscountTotal = comboDiscount,
            OrderDiscount = orderDiscount,
            VoucherDiscountTotal = voucherDiscount,
            GrandTotal = grandTotal,
            PaidTotal = grandTotal,
            Lines = new List<OrderLine> { line }
        };
    }

    private sealed class FakeAllocationRepository : IOrderLegalEntityAllocationRepository
    {
        public required Store Store { get; init; }
        public List<LegalEntity> LegalEntities { get; init; } = [];
        public List<InventoryBalance> Balances { get; init; } = [];
        public List<OrderLegalEntityAllocation> Added { get; } = [];
        public List<string> Events { get; } = [];
        public int LockCallCount { get; private set; }
        public bool HasExistingAllocation { get; set; }

        public Task<Store?> GetStoreFeatureStateAsync(int storeId, CancellationToken ct = default)
            => Task.FromResult<Store?>(Store.Id == storeId ? Store : null);

        public Task<List<LegalEntity>> GetActiveSalesLegalEntitiesAsync(
            int storeId,
            CancellationToken ct = default)
            => Task.FromResult(LegalEntities.Where(x => x.StoreId == storeId).ToList());

        public Task<List<InventoryBalance>> LockInventoryForAllocationAsync(
            int storeId,
            IReadOnlyCollection<int> warehouseIds,
            IReadOnlyCollection<int> productVariantIds,
            CancellationToken ct = default)
        {
            LockCallCount++;
            Events.Add("lock");
            return Task.FromResult(Balances.Where(x =>
                x.StoreId == storeId &&
                warehouseIds.Contains(x.WarehouseId) &&
                productVariantIds.Contains(x.ProductVariantId)).ToList());
        }

        public Task<bool> AnyForOrderAsync(int orderId, CancellationToken ct = default)
            => Task.FromResult(HasExistingAllocation);

        public Task<List<OrderLegalEntityAllocation>> GetForOrderAsync(
            int orderId,
            CancellationToken ct = default)
            => Task.FromResult(Added.Where(x => x.OrderId == orderId).ToList());

        public Task AddRangeAsync(
            IReadOnlyCollection<OrderLegalEntityAllocation> allocations,
            CancellationToken ct = default)
        {
            Added.AddRange(allocations);
            Events.Add("add");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeInventoryMovementService : IInventoryMovementService
    {
        private readonly FakeAllocationRepository _repository;
        private int _nextTransactionId = 9000;

        public FakeInventoryMovementService(FakeAllocationRepository repository)
            => _repository = repository;

        public List<CreateInventoryMovementRequest> Requests { get; } = [];
        public List<List<InventoryPostingLockKey>> PreLockBatches { get; } = [];
        public bool ReturnConflict { get; init; }

        public Task PreLockBalancesAsync(
            IEnumerable<InventoryPostingLockKey> keys,
            CancellationToken ct = default)
        {
            PreLockBatches.Add(keys.ToList());
            _repository.Events.Add("prelock");
            return Task.CompletedTask;
        }

        public Task<InventoryMovementResultDto> CreateAsync(
            CreateInventoryMovementRequest request,
            CancellationToken ct = default)
        {
            Requests.Add(request);
            _repository.Events.Add($"movement:{request.WarehouseId}");

            if (ReturnConflict)
            {
                return Task.FromResult(new InventoryMovementResultDto
                {
                    WarehouseId = request.WarehouseId,
                    ProductVariantId = request.ProductVariantId,
                    IsSkipped = true,
                    IsCreated = false
                });
            }

            var balance = _repository.Balances.Single(x =>
                x.WarehouseId == request.WarehouseId &&
                x.ProductVariantId == request.ProductVariantId);
            var before = balance.OnHandQty;
            var after = before + request.QuantityChange;
            balance.OnHandQty = after;
            var unitCost = request.WarehouseId == 11 ? 10m : 20m;

            return Task.FromResult(new InventoryMovementResultDto
            {
                WarehouseId = request.WarehouseId,
                ProductVariantId = request.ProductVariantId,
                BeforeQty = before,
                QuantityChange = request.QuantityChange,
                AfterQty = after,
                IsNegativeAfterTransaction = after < 0m,
                IsCreated = true,
                InventoryTransactionId = ++_nextTransactionId,
                ValueChange = request.QuantityChange * unitCost,
                ActualQuantity = Math.Abs(request.QuantityChange),
                ActualValueChange = request.QuantityChange * unitCost
            });
        }

        public Task<decimal> PeekOutboundUnitCostAsync(
            int warehouseId,
            int productVariantId,
            decimal quantity,
            CancellationToken ct = default)
            => Task.FromResult(warehouseId == 11 ? 10m : 20m);
    }
}
