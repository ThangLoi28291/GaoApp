using FluentAssertions;
using GaoApp.Application.DTOs.Orders.LegalEntityAllocation;
using GaoApp.Application.Services.Orders;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Inventory;

public sealed class OrderLegalEntityAllocationServiceTests
{
    private readonly OrderLegalEntityAllocationService _service = new();

    [Fact]
    public void Preview_Hkd1HasEnough_ShouldAllocateOnlyHkd1WithoutMutatingSnapshot()
    {
        var inventory = Inventory(11, 101, onHand: 10m);
        var request = Request(
            new[] { Line(1, 101, quantity: 8m) },
            DefaultSources(),
            new[] { inventory });

        var result = _service.Preview(request);

        result.IsSuccess.Should().BeTrue();
        result.Allocations.Should().ContainSingle();
        result.Allocations[0].LegalEntityId.Should().Be(1);
        result.Allocations[0].WarehouseId.Should().Be(11);
        result.Allocations[0].BaseQuantity.Should().Be(8m);
        result.HasMultipleLegalEntities.Should().BeFalse();
        inventory.OnHandBaseQuantity.Should().Be(10m);
        inventory.ReservedBaseQuantity.Should().Be(0m);
    }

    [Fact]
    public void Preview_Hkd1IsShortAndHkd2HasEnough_ShouldSplitInPriorityOrder()
    {
        var request = Request(
            new[] { Line(1, 101, quantity: 8m) },
            DefaultSources(),
            new[]
            {
                Inventory(11, 101, onHand: 5m),
                Inventory(22, 101, onHand: 20m)
            });

        var result = _service.Preview(request);

        result.IsSuccess.Should().BeTrue();
        result.Allocations.Select(x => (x.LegalEntityId, x.BaseQuantity))
            .Should().Equal((1, 5m), (2, 3m));
        result.LegalEntityCount.Should().Be(2);
        result.HasMultipleLegalEntities.Should().BeTrue();
        result.TotalShortageBaseQuantity.Should().Be(0m);
    }

    [Fact]
    public void Preview_Hkd1HasZero_ShouldAllocateOnlyHkd2()
    {
        var request = Request(
            new[] { Line(1, 101, quantity: 8m) },
            DefaultSources(),
            new[]
            {
                Inventory(11, 101, onHand: 0m),
                Inventory(22, 101, onHand: 20m)
            });

        var result = _service.Preview(request);

        result.IsSuccess.Should().BeTrue();
        result.Allocations.Should().ContainSingle(x =>
            x.LegalEntityId == 2 && x.WarehouseId == 22 && x.BaseQuantity == 8m);
    }

    [Fact]
    public void Preview_TotalStockIsInsufficient_ShouldReturnClearShortageAndPartialPreview()
    {
        var request = Request(
            new[] { Line(1, 101, quantity: 8m) },
            DefaultSources(),
            new[]
            {
                Inventory(11, 101, onHand: 2m),
                Inventory(22, 101, onHand: 3m)
            });

        var result = _service.Preview(request);

        result.IsSuccess.Should().BeFalse();
        result.TotalRequiredBaseQuantity.Should().Be(8m);
        result.TotalAllocatedBaseQuantity.Should().Be(5m);
        result.TotalShortageBaseQuantity.Should().Be(3m);
        result.Shortages.Should().ContainSingle(x =>
            x.OrderLineId == 1 &&
            x.ProductVariantId == 101 &&
            x.ShortageBaseQuantity == 3m);
        result.Message.Should().Contain("Không đủ tồn");
        result.Shortages[0].Message.Should().Contain("sản phẩm #101");
    }

    [Fact]
    public void Preview_AllowNegative_ShouldAssignShortageToLastPriorityHkd()
    {
        var request = new OrderLegalEntityAllocationRequest
        {
            StoreId = 7,
            OrderId = 1001,
            AllowNegativeInventory = true,
            Lines = new[] { Line(1, 101, quantity: 8m) },
            EligibleSources = DefaultSources(),
            Inventory = new[]
            {
                Inventory(11, 101, onHand: 2m),
                Inventory(22, 101, onHand: 3m)
            }
        };

        var result = _service.Preview(request);

        result.IsSuccess.Should().BeTrue();
        result.TotalShortageBaseQuantity.Should().Be(3m);
        result.TotalAllocatedBaseQuantity.Should().Be(8m);
        result.Allocations.Select(x => (x.LegalEntityId, x.BaseQuantity, x.AllocationSource))
            .Should().Equal(
                (1, 2m, OrderLegalEntityAllocationSource.AutoBySalePriority),
                (2, 6m, OrderLegalEntityAllocationSource.AutoNegativeFallback));
    }

    [Fact]
    public void Preview_MultipleVariants_ShouldAllocateEachOrderLineIndependently()
    {
        var request = Request(
            new[]
            {
                Line(1, 101, quantity: 6m),
                Line(2, 202, quantity: 4m)
            },
            DefaultSources(),
            new[]
            {
                Inventory(11, 101, onHand: 2m),
                Inventory(22, 101, onHand: 10m),
                Inventory(11, 202, onHand: 4m),
                Inventory(22, 202, onHand: 10m)
            });

        var result = _service.Preview(request);

        result.IsSuccess.Should().BeTrue();
        result.Allocations.Where(x => x.OrderLineId == 1)
            .Select(x => (x.LegalEntityId, x.BaseQuantity))
            .Should().Equal((1, 2m), (2, 4m));
        result.Allocations.Where(x => x.OrderLineId == 2)
            .Should().ContainSingle(x => x.LegalEntityId == 1 && x.BaseQuantity == 4m);
    }

    [Fact]
    public void Preview_RepeatedVariantAcrossLines_ShouldNotReuseLocalRemainingStock()
    {
        var request = Request(
            new[]
            {
                // Cố ý truyền ngược để xác nhận engine luôn xử lý ổn định theo OrderLineId.
                Line(2, 101, quantity: 4m),
                Line(1, 101, quantity: 4m)
            },
            DefaultSources(),
            new[]
            {
                Inventory(11, 101, onHand: 5m),
                Inventory(22, 101, onHand: 5m)
            });

        var result = _service.Preview(request);

        result.IsSuccess.Should().BeTrue();
        result.Allocations.Where(x => x.OrderLineId == 1)
            .Should().ContainSingle(x => x.LegalEntityId == 1 && x.BaseQuantity == 4m);
        result.Allocations.Where(x => x.OrderLineId == 2)
            .Select(x => (x.LegalEntityId, x.BaseQuantity))
            .Should().Equal((1, 1m), (2, 3m));
    }

    [Fact]
    public void Preview_ShouldUseOnHandMinusReservedAsAvailableQuantity()
    {
        var request = Request(
            new[] { Line(1, 101, quantity: 8m) },
            DefaultSources(),
            new[]
            {
                Inventory(11, 101, onHand: 10m, reserved: 4m),
                Inventory(22, 101, onHand: 10m)
            });

        var result = _service.Preview(request);

        result.Allocations.Select(x => (x.LegalEntityId, x.BaseQuantity))
            .Should().Equal((1, 6m), (2, 2m));
    }

    [Fact]
    public void Preview_ShouldSortBySalePriorityInsteadOfInputOrder()
    {
        var request = Request(
            new[] { Line(1, 101, quantity: 3m) },
            DefaultSources().Reverse().ToArray(),
            new[]
            {
                Inventory(11, 101, onHand: 10m),
                Inventory(22, 101, onHand: 10m)
            });

        var result = _service.Preview(request);

        result.Allocations.Should().ContainSingle(x =>
            x.LegalEntityId == 1 &&
            x.SalePriority == 1 &&
            x.AllocationSource == OrderLegalEntityAllocationSource.AutoBySalePriority);
    }

    [Fact]
    public void Preview_ConvertedSellingUnit_ShouldPreserveQuantityAndBaseQuantity()
    {
        var request = Request(
            new[] { Line(1, 101, quantity: 3m, multiplier: 4m, conversionId: 501) },
            DefaultSources(),
            new[]
            {
                Inventory(11, 101, onHand: 8m),
                Inventory(22, 101, onHand: 4m)
            });

        var result = _service.Preview(request);

        result.IsSuccess.Should().BeTrue();
        result.Allocations.Select(x => (x.LegalEntityId, x.Quantity, x.BaseQuantity))
            .Should().Equal((1, 2m, 8m), (2, 1m, 4m));
        result.Allocations.Should().OnlyContain(x => x.ProductUnitConversionId == 501);
    }

    [Fact]
    public void Preview_InvalidBaseQuantity_ShouldRejectBeforeAllocation()
    {
        var invalidLine = new OrderLegalEntityAllocationLineInput
        {
            OrderLineId = 1,
            ProductVariantId = 101,
            Quantity = 3m,
            Multiplier = 4m,
            BaseQuantity = 11m
        };
        var request = Request(
            new[] { invalidLine },
            DefaultSources(),
            new[] { Inventory(11, 101, onHand: 20m) });

        var action = () => _service.Preview(request);

        action.Should().Throw<ArgumentException>()
            .WithMessage("*BaseQuantity không bằng Quantity × Multiplier*");
    }

    private static OrderLegalEntityAllocationRequest Request(
        IReadOnlyCollection<OrderLegalEntityAllocationLineInput> lines,
        IReadOnlyCollection<LegalEntityAllocationSourceInput> sources,
        IReadOnlyCollection<LegalEntityInventoryAvailabilityInput> inventory)
        => new()
        {
            StoreId = 7,
            OrderId = 1001,
            Lines = lines,
            EligibleSources = sources,
            Inventory = inventory
        };

    private static OrderLegalEntityAllocationLineInput Line(
        int lineId,
        int variantId,
        decimal quantity,
        decimal multiplier = 1m,
        int? conversionId = null)
        => new()
        {
            OrderLineId = lineId,
            ProductVariantId = variantId,
            ProductUnitConversionId = conversionId,
            Quantity = quantity,
            Multiplier = multiplier,
            BaseQuantity = quantity * multiplier
        };

    private static LegalEntityAllocationSourceInput[] DefaultSources()
        =>
        [
            new() { LegalEntityId = 1, WarehouseId = 11, SalePriority = 1 },
            new() { LegalEntityId = 2, WarehouseId = 22, SalePriority = 2 }
        ];

    private static LegalEntityInventoryAvailabilityInput Inventory(
        int warehouseId,
        int variantId,
        decimal onHand,
        decimal reserved = 0m)
        => new()
        {
            WarehouseId = warehouseId,
            ProductVariantId = variantId,
            OnHandBaseQuantity = onHand,
            ReservedBaseQuantity = reserved
        };
}
