using FluentAssertions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Repositories.Orders;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Inventory;

public sealed class ReturnableValuationFragmentServiceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ReversalLedger_ShouldConsumeNoRestockWithoutDoubleCountingRestock(
        int valuationReverseQuantity)
    {
        await using var context = CreateContext();
        var occurredAtUtc = DateTime.UtcNow.AddMinutes(-5);
        context.InventoryValuationEntries.Add(new InventoryValuationEntry
        {
            Id = 100,
            StoreId = 1,
            InventoryTransactionId = 501,
            WarehouseId = 11,
            ProductVariantId = 99,
            EntryType = InventoryValuationEntryType.Outbound,
            ReferenceType = InventoryReferenceType.Order,
            ReferenceId = "700",
            ReferenceLineId = 701,
            ReferenceSubKey = "SALE-SOURCE",
            Quantity = -5m,
            UnitCost = 10m,
            Amount = -50m,
            OccurredAtUtc = occurredAtUtc,
            RowVersion = new byte[8]
        });
        if (valuationReverseQuantity > 0)
        {
            context.InventoryValuationEntries.Add(new InventoryValuationEntry
            {
                Id = 101,
                StoreId = 1,
                InventoryTransactionId = 601,
                WarehouseId = 11,
                ProductVariantId = 99,
                EntryType = InventoryValuationEntryType.Inbound,
                ReferenceType = InventoryReferenceType.Refund,
                ReferenceId = "800",
                ReferenceLineId = 801,
                Quantity = valuationReverseQuantity,
                UnitCost = 10m,
                Amount = valuationReverseQuantity * 10m,
                SourceValuationEntryId = 100,
                OccurredAtUtc = occurredAtUtc.AddMinutes(1),
                RowVersion = new byte[8]
            });
        }

        context.OrderLegalEntityAllocationReversals.Add(
            new OrderLegalEntityAllocationReversal
            {
                Id = 1,
                StoreId = 1,
                OrderId = 700,
                OrderLineId = 701,
                OrderLegalEntityAllocationId = 900,
                LegalEntityId = 1,
                WarehouseId = 11,
                ProductVariantId = 99,
                SourceValuationEntryId = 100,
                ReversalType = valuationReverseQuantity > 0
                    ? OrderLegalEntityReversalType.ReturnRestock
                    : OrderLegalEntityReversalType.ReturnNoRestock,
                BaseQuantity = 2m,
                FinancialAmount = 20m,
                OccurredAtUtc = occurredAtUtc.AddMinutes(1),
                RowVersion = new byte[8]
            });
        await context.SaveChangesAsync();

        var service = new ReturnableValuationFragmentService(
            new InventoryValuationEntryRepository(context),
            new OrderLegalEntityAllocationReversalRepository(context));

        var result = await service.GetForOrderLineAsync(700, 701);

        result.Should().ContainSingle();
        result[0].ReversedQuantityAbs.Should().Be(2m);
        result[0].RemainingQuantityAbs.Should().Be(3m);
        result[0].InventoryTransactionId.Should().Be(501);
        result[0].WarehouseId.Should().Be(11);
    }

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
        public string? UserName => "phase-22-6-returnable-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
