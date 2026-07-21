using FluentAssertions;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions.Pos;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Services.Inventory;
using GaoApp.Application.Services.Orders;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Repositories.Orders;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Repositories.Orders;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Inventory;

public sealed class InventoryReservationServiceTests
{
    [Fact]
    public async Task ReserveForOrder_MultiLegalEntity_ShouldSplitReservationByPriority()
    {
        await using var context = CreateContext();
        await SeedMultiLegalEntityAsync(context, firstOnHand: 2m, secondOnHand: 3m);
        var order = MultiLegalEntityOrder(quantity: 4m);

        var service = CreateService(context);
        await service.ReserveForOrderAsync(order);

        var reservations = await context.InventoryReservations
            .Where(x => x.ReferenceId == order.Id.ToString())
            .OrderBy(x => x.WarehouseId)
            .ToListAsync();
        reservations.Should().HaveCount(2);
        reservations[0].WarehouseId.Should().Be(11);
        reservations[0].ReservedQty.Should().Be(2m);
        reservations[1].WarehouseId.Should().Be(22);
        reservations[1].ReservedQty.Should().Be(2m);
        (await context.InventoryBalances.SingleAsync(x => x.WarehouseId == 11))
            .ReservedQty.Should().Be(2m);
        (await context.InventoryBalances.SingleAsync(x => x.WarehouseId == 22))
            .ReservedQty.Should().Be(2m);
        order.HasReservation.Should().BeTrue();
        order.ReservedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task ReserveForOrder_MultiLegalEntityShortage_ShouldNotLeavePartialReservation()
    {
        await using var context = CreateContext();
        await SeedMultiLegalEntityAsync(context, firstOnHand: 1m, secondOnHand: 1m);
        var order = MultiLegalEntityOrder(quantity: 3m);
        var service = CreateService(context);

        Func<Task> action = () => service.ReserveForOrderAsync(order);

        var exception = await action.Should().ThrowAsync<PosAppException>();
        exception.Which.ErrorCode.Should().Be(
            PosErrorCodes.CheckoutLegalEntityInsufficientInventory);
        (await context.InventoryReservations.CountAsync()).Should().Be(0);
        (await context.InventoryBalances.SumAsync(x => x.ReservedQty)).Should().Be(0m);
        order.HasReservation.Should().BeFalse();
    }

    [Fact]
    public async Task ReserveForOrder_CapturedMultiThenKillSwitch_ShouldKeepMultiReservation()
    {
        await using var context = CreateContext();
        await SeedMultiLegalEntityAsync(context, firstOnHand: 2m, secondOnHand: 3m);
        var store = await context.Stores.SingleAsync(x => x.Id == 1);
        var activationAt = store.MultiLegalEntityActivatedAtUtc;
        store.IsMultiLegalEntityEnabled = false;
        store.MultiLegalEntityActivatedAtUtc = null;
        await context.SaveChangesAsync();
        var order = MultiLegalEntityOrder(quantity: 4m);
        order.UseMultiLegalEntity = true;
        order.LegalEntityModeCapturedAtUtc = DateTime.UtcNow.AddMinutes(-10);
        order.LegalEntityActivationAtUtcSnapshot = activationAt;

        await CreateService(context).ReserveForOrderAsync(order);

        var reservations = await context.InventoryReservations
            .Where(x => x.ReferenceId == order.Id.ToString())
            .OrderBy(x => x.WarehouseId)
            .ToListAsync();
        reservations.Select(x => (x.WarehouseId, x.ReservedQty))
            .Should().Equal((11, 2m), (22, 2m));
    }

    [Fact]
    public async Task ReleaseForOrder_DraftResumedFromHold_ShouldReleaseActiveReservation()
    {
        await using var context = CreateContext();
        var reservedAt = DateTime.UtcNow.AddMinutes(-10);
        var order = Order(id: 401, hasReservation: true, reservedAt: reservedAt);
        var balance = Balance(reservedQty: 1m);
        var reservation = Reservation(quantity: 1m);

        context.InventoryBalances.Add(balance);
        context.InventoryReservations.Add(reservation);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        await service.ReleaseForOrderAsync(order, "Hủy giỏ sau khi mở lại đơn giữ.");

        balance.ReservedQty.Should().Be(0m);
        reservation.Status.Should().Be(InventoryReservationStatus.Released);
        reservation.ReleasedAtUtc.Should().NotBeNull();
        reservation.ReleaseNote.Should().Be("Hủy giỏ sau khi mở lại đơn giữ.");
        order.HasReservation.Should().BeFalse();
        order.ReservedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task ConsumeForOrder_ShouldClearReservedQuantityAndOrderMarker()
    {
        await using var context = CreateContext();
        var order = Order(
            id: 401,
            hasReservation: true,
            reservedAt: DateTime.UtcNow.AddMinutes(-5));
        var balance = Balance(reservedQty: 2m);
        var reservation = Reservation(quantity: 2m);

        context.InventoryBalances.Add(balance);
        context.InventoryReservations.Add(reservation);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        await service.ConsumeForOrderAsync(order);

        balance.ReservedQty.Should().Be(0m);
        reservation.Status.Should().Be(InventoryReservationStatus.Consumed);
        reservation.ReleasedAtUtc.Should().NotBeNull();
        order.HasReservation.Should().BeFalse();
        order.ReservedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task ReleaseForOrder_NoActiveRows_ShouldStillClearStaleOrderMarker()
    {
        await using var context = CreateContext();
        var order = Order(
            id: 401,
            hasReservation: true,
            reservedAt: DateTime.UtcNow.AddMinutes(-5));

        var service = CreateService(context);
        await service.ReleaseForOrderAsync(order, "Không còn reservation active.");

        order.HasReservation.Should().BeFalse();
        order.ReservedAtUtc.Should().BeNull();
    }

    private static InventoryReservationService CreateService(InMemoryAppDbContext context)
        => new(
            new InventoryReservationRepository(context),
            new InventoryBalanceRepository(context),
            new POSShiftRepository(context),
            new WarehouseRepository(context),
            new OrderLegalEntityAllocationRepository(context),
            new OrderLegalEntityAllocationService());

    private static Order Order(int id, bool hasReservation, DateTime? reservedAt)
        => new()
        {
            Id = id,
            StoreId = 1,
            Status = OrderStatus.Draft,
            HasReservation = hasReservation,
            ReservedAtUtc = reservedAt,
            RowVersion = new byte[8]
        };

    private static Order MultiLegalEntityOrder(decimal quantity)
        => new()
        {
            Id = 501,
            StoreId = 1,
            Status = OrderStatus.Draft,
            Lines = new List<OrderLine>
            {
                new()
                {
                    Id = 901,
                    StoreId = 1,
                    OrderId = 501,
                    ProductId = 10,
                    VariantId = 27764,
                    ItemName = "Multi HKD reservation",
                    Quantity = quantity,
                    Multiplier = 1m,
                    BaseQuantity = quantity
                }
            },
            RowVersion = new byte[8]
        };

    private static async Task SeedMultiLegalEntityAsync(
        InMemoryAppDbContext context,
        decimal firstOnHand,
        decimal secondOnHand)
    {
        context.Stores.Add(new Store
        {
            Id = 1,
            Name = "Phase 22.6 Store",
            SubDomain = "store-one",
            SubDomainNormalized = "STORE-ONE",
            IsActive = true,
            IsMultiLegalEntityEnabled = true,
            MultiLegalEntityActivatedAtUtc = DateTime.UtcNow.AddDays(-1),
            RowVersion = new byte[8]
        });

        var firstWarehouse = new Warehouse
        {
            Id = 11,
            StoreId = 1,
            LegalEntityId = 1,
            Code = "WH1",
            Name = "Kho HKD 1",
            IsActive = true,
            RowVersion = new byte[8]
        };
        var secondWarehouse = new Warehouse
        {
            Id = 22,
            StoreId = 1,
            LegalEntityId = 2,
            Code = "WH2",
            Name = "Kho HKD 2",
            IsActive = true,
            RowVersion = new byte[8]
        };
        context.LegalEntities.AddRange(
            new LegalEntity
            {
                Id = 1,
                StoreId = 1,
                Code = "HKD1",
                Name = "HKD 1",
                LegalName = "Hộ kinh doanh 1",
                SalePriority = 1,
                IsActive = true,
                DefaultWarehouseId = 11,
                DefaultWarehouse = firstWarehouse,
                RowVersion = new byte[8]
            },
            new LegalEntity
            {
                Id = 2,
                StoreId = 1,
                Code = "HKD2",
                Name = "HKD 2",
                LegalName = "Hộ kinh doanh 2",
                SalePriority = 2,
                IsActive = true,
                DefaultWarehouseId = 22,
                DefaultWarehouse = secondWarehouse,
                RowVersion = new byte[8]
            });
        context.InventoryBalances.AddRange(
            new InventoryBalance
            {
                Id = 101,
                StoreId = 1,
                WarehouseId = 11,
                ProductVariantId = 27764,
                OnHandQty = firstOnHand,
                RowVersion = new byte[8]
            },
            new InventoryBalance
            {
                Id = 102,
                StoreId = 1,
                WarehouseId = 22,
                ProductVariantId = 27764,
                OnHandQty = secondOnHand,
                RowVersion = new byte[8]
            });
        await context.SaveChangesAsync();
    }

    private static InventoryBalance Balance(decimal reservedQty)
        => new()
        {
            StoreId = 1,
            WarehouseId = 7,
            ProductVariantId = 27764,
            OnHandQty = -5m,
            ReservedQty = reservedQty,
            RowVersion = new byte[8]
        };

    private static InventoryReservation Reservation(decimal quantity)
        => new()
        {
            StoreId = 1,
            WarehouseId = 7,
            ProductVariantId = 27764,
            ReferenceType = InventoryReferenceType.Order,
            ReferenceId = "401",
            ReferenceLineId = 806,
            ReservedQty = quantity,
            Status = InventoryReservationStatus.Active,
            ReservedAtUtc = DateTime.UtcNow.AddMinutes(-10),
            RowVersion = new byte[8]
        };

    private static InMemoryAppDbContext CreateContext()
    {
        var tenant = new TenantContext();
        tenant.SetStore(1, "store-one");
        var options = new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new InMemoryAppDbContext(options, tenant, new TestCurrentUser());
        context.VerifyRowVersionConfiguration();
        return context;
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId => 99;
        public string? UserName => "phase-22-5-reservation-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
