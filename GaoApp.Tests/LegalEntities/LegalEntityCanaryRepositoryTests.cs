using FluentAssertions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.LegalEntities;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.LegalEntities;

public sealed class LegalEntityCanaryRepositoryTests
{
    [Fact]
    public async Task GetMismatchCounts_ShouldUseSameOrderAndInvoiceToleranceRules()
    {
        await using var context = CreateContext();
        var completedAt = DateTime.UtcNow.AddMinutes(-5);
        context.Stores.Add(new Store
        {
            Id = 1,
            Name = "Canary Store",
            SubDomain = "canary",
            SubDomainNormalized = "CANARY",
            IsActive = true,
            RowVersion = new byte[8]
        });
        context.ProductVariants.Add(new ProductVariant
        {
            Id = 10,
            StoreId = 1,
            ProductId = 100,
            Sku = "CANARY-10",
            HasInputInvoice = true,
            RowVersion = new byte[8]
        });
        context.Orders.AddRange(
            CompletedOrder(1, completedAt, 100m),
            CompletedOrder(2, completedAt, 100m));
        context.OrderLegalEntityAllocations.AddRange(
            Allocation(1, orderId: 1, netAmount: 90m),
            Allocation(2, orderId: 2, netAmount: 100m));
        context.InvoiceHeads.AddRange(
            Invoice(1, orderId: 1, grandTotal: 90m),
            Invoice(2, orderId: 2, grandTotal: 80m));
        await context.SaveChangesAsync();

        var result = await new LegalEntityCanaryRepository(context)
            .GetReconciliationMismatchCountsAsync(
                1,
                completedAt.AddMinutes(-1),
                completedAt.AddMinutes(1));

        result.AllocationMismatchCount.Should().Be(1);
        result.InvoiceMismatchCount.Should().Be(1);
    }

    private static Order CompletedOrder(int id, DateTime completedAt, decimal grandTotal)
        => new()
        {
            Id = id,
            StoreId = 1,
            POSShiftId = 1,
            Status = OrderStatus.Completed,
            CompletedAtUtc = completedAt,
            GrandTotal = grandTotal,
            RowVersion = new byte[8]
        };

    private static OrderLegalEntityAllocation Allocation(int id, int orderId, decimal netAmount)
        => new()
        {
            Id = id,
            StoreId = 1,
            OrderId = orderId,
            OrderLineId = id,
            ProductVariantId = 10,
            LegalEntityId = 1,
            WarehouseId = 1,
            Quantity = 1m,
            BaseQuantity = 1m,
            LineTotal = netAmount,
            NetAmount = netAmount,
            RowVersion = new byte[8]
        };

    private static InvoiceHead Invoice(int id, int orderId, decimal grandTotal)
        => new()
        {
            Id = id,
            StoreId = 1,
            OrderId = orderId,
            GrandTotal = grandTotal,
            RowVersion = new byte[8]
        };

    private static InMemoryAppDbContext CreateContext()
    {
        var tenant = new TenantContext();
        tenant.SetStore(1, "canary");
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
        public string? UserName => "canary-repository-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
