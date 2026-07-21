using FluentAssertions;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Invoices;
using GaoApp.Infrastructure.Repositories.Orders;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Invoices;

public sealed class DraftInvoiceReturnSyncServiceTests
{
    [Fact]
    public async Task DraftHeads_ShouldReduceOnlyTheHkdAllocationActuallyReversed()
    {
        await using var context = CreateContext();
        await SeedSplitInvoiceAndReturnAsync(context);
        var service = CreateService(context);

        await service.EnsurePosReturnAllowedAsync(1, 41, new[] { 51 });
        await service.SyncAfterReturnAsync(41, 81);

        var hkd1 = await context.InvoiceHeads
            .Include(x => x.Details)
            .SingleAsync(x => x.Id == 71);
        var hkd2 = await context.InvoiceHeads
            .Include(x => x.Details)
            .SingleAsync(x => x.Id == 72);

        hkd1.Details.Single().Quantity.Should().Be(3m);
        hkd1.GrandTotal.Should().Be(300_000m);
        hkd2.Details.Single().Quantity.Should().Be(1m);
        hkd2.Details.Single().Amount.Should().Be(100_000m);
        hkd2.TotalQuantity.Should().Be(1m);
        hkd2.GrandTotal.Should().Be(100_000m);
    }

    [Fact]
    public async Task IssuedHeadContainingReturnedLine_ShouldRequireAccountingReturn()
    {
        await using var context = CreateContext();
        await SeedSplitInvoiceAndReturnAsync(context);
        var issued = await context.InvoiceHeads.SingleAsync(x => x.Id == 72);
        issued.ProviderStatus = InvoiceProviderStatus.Issued;
        issued.ProviderInvoiceNo = "00000072";
        issued.IssuedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync();
        var service = CreateService(context);

        var act = () => service.EnsurePosReturnAllowedAsync(1, 41, new[] { 51 });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*nghiệp vụ kế toán/hóa đơn điện tử*");
    }

    private static DraftInvoiceReturnSyncService CreateService(InMemoryAppDbContext context)
        => new(
            new InvoiceRepository(context),
            new InvoiceInputStockRepository(context),
            new OrderLegalEntityAllocationReversalRepository(context),
            new SalesReturnRepository(context));

    private static async Task SeedSplitInvoiceAndReturnAsync(InMemoryAppDbContext context)
    {
        context.Stores.Add(new Store
        {
            Id = 1,
            Name = "store-one",
            SubDomain = "store-one",
            SubDomainNormalized = "STORE-ONE",
            IsActive = true,
            RowVersion = new byte[8]
        });
        context.ProductVariants.Add(new ProductVariant
        {
            Id = 31,
            StoreId = 1,
            ProductId = 1,
            Sku = "RETURN-SYNC-31",
            ProductVariantName = "Sản phẩm trả hàng",
            HasInputInvoice = true,
            IsActive = true,
            RowVersion = new byte[8]
        });
        context.Orders.Add(new Order
        {
            Id = 41,
            StoreId = 1,
            POSShiftId = 21,
            Status = OrderStatus.Completed,
            GrandTotal = 500_000m,
            RowVersion = new byte[8]
        });
        context.OrderLines.Add(new OrderLine
        {
            Id = 51,
            StoreId = 1,
            OrderId = 41,
            ProductId = 1,
            VariantId = 31,
            ItemName = "Sản phẩm trả hàng",
            Quantity = 5m,
            BaseQuantity = 5m,
            Multiplier = 1m,
            UnitPrice = 100_000m,
            LineTotal = 500_000m,
            RowVersion = new byte[8]
        });

        var allocation1 = Allocation(61, legalEntityId: 1, warehouseId: 11, priority: 1, quantity: 3m);
        var allocation2 = Allocation(62, legalEntityId: 2, warehouseId: 22, priority: 2, quantity: 2m);
        context.OrderLegalEntityAllocations.AddRange(allocation1, allocation2);

        context.InvoiceHeads.AddRange(
            Head(71, legalEntityId: 1, quantity: 3m, amount: 300_000m),
            Head(72, legalEntityId: 2, quantity: 2m, amount: 200_000m));
        context.InvoiceDetails.AddRange(
            Detail(73, invoiceHeadId: 71, allocationId: 61, quantity: 3m, amount: 300_000m),
            Detail(74, invoiceHeadId: 72, allocationId: 62, quantity: 2m, amount: 200_000m));

        context.SalesReturns.Add(new SalesReturn
        {
            Id = 81,
            StoreId = 1,
            OrderId = 41,
            POSShiftId = 21,
            ReturnNumber = "RET-81",
            Type = SalesReturnType.ReturnOnly,
            Status = SalesReturnStatus.Completed,
            Reason = "Khách trả 1",
            CreatedByUserId = 1,
            CompletedByUserId = 1,
            CompletedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[8]
        });
        context.SalesReturnLines.Add(new SalesReturnLine
        {
            Id = 82,
            StoreId = 1,
            SalesReturnId = 81,
            OrderLineId = 51,
            ProductId = 1,
            VariantId = 31,
            ItemName = "Sản phẩm trả hàng",
            ReturnQuantity = 1m,
            ReturnBaseQuantity = 1m,
            Action = SalesReturnLineAction.Restock,
            RowVersion = new byte[8]
        });
        context.OrderLegalEntityAllocationReversals.Add(new OrderLegalEntityAllocationReversal
        {
            Id = 91,
            StoreId = 1,
            OrderId = 41,
            OrderLineId = 51,
            OrderLegalEntityAllocationId = 62,
            SalesReturnId = 81,
            SalesReturnLineId = 82,
            LegalEntityId = 2,
            WarehouseId = 22,
            ProductVariantId = 31,
            SourceValuationEntryId = 9002,
            ReversalType = OrderLegalEntityReversalType.ReturnRestock,
            BaseQuantity = 1m,
            FinancialAmount = 100_000m,
            RowVersion = new byte[8]
        });

        await context.SaveChangesAsync();
    }

    private static OrderLegalEntityAllocation Allocation(
        int id,
        int legalEntityId,
        int warehouseId,
        int priority,
        decimal quantity)
        => new()
        {
            Id = id,
            StoreId = 1,
            OrderId = 41,
            OrderLineId = 51,
            ProductVariantId = 31,
            LegalEntityId = legalEntityId,
            WarehouseId = warehouseId,
            SalePriority = priority,
            Quantity = quantity,
            BaseQuantity = quantity,
            UnitPrice = 100_000m,
            LineTotal = quantity * 100_000m,
            NetAmount = quantity * 100_000m,
            RowVersion = new byte[8]
        };

    private static InvoiceHead Head(
        int id,
        int legalEntityId,
        decimal quantity,
        decimal amount)
        => new()
        {
            Id = id,
            StoreId = 1,
            OrderId = 41,
            LegalEntityId = legalEntityId,
            ProviderStatus = InvoiceProviderStatus.LocalDraft,
            TotalQuantity = quantity,
            SubTotal = amount,
            GrandTotal = amount,
            RowVersion = new byte[8]
        };

    private static InvoiceDetail Detail(
        int id,
        int invoiceHeadId,
        int allocationId,
        decimal quantity,
        decimal amount)
        => new()
        {
            Id = id,
            StoreId = 1,
            InvoiceHeadId = invoiceHeadId,
            OrderLineId = 51,
            OrderLegalEntityAllocationId = allocationId,
            ProductVariantId = 31,
            SourceType = InvoiceDetailSourceType.FromOrderLine,
            ItemName = "Sản phẩm trả hàng",
            Quantity = quantity,
            UnitPrice = 100_000m,
            Amount = amount,
            TotalAmount = amount,
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
        public int? UserId => 1;
        public string? UserName => "draft-invoice-return-sync-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
