using FluentAssertions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.LegalEntities;
using GaoApp.Application.Services.LegalEntities;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.LegalEntities;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.LegalEntities;

public sealed class LegalEntityReconciliationTests
{
    [Fact]
    public async Task Order_breakdown_should_show_split_warehouses_and_child_invoices()
    {
        await using var context = CreateContext();
        var data = await SeedSplitOrderAsync(context, withReturn: false, invoiceSyncedAfterReturn: true);
        var service = CreateService(context);

        var result = await service.GetOrderBreakdownAsync(data.Order.Id);

        result.Should().NotBeNull();
        result!.EntitySummaries.Should().HaveCount(2);
        result.Allocations.Should().HaveCount(2);
        result.Allocations.Select(x => x.WarehouseCode).Should().Equal("KHO-1", "KHO-2");
        result.Invoices.Should().HaveCount(2);
        result.AllocationGrossTotal.Should().Be(280_000m);
        result.ActualInvoiceTotal.Should().Be(280_000m);
        result.IsBalanced.Should().BeTrue();
    }

    [Fact]
    public async Task Report_should_reconcile_partial_return_against_synced_draft_invoices()
    {
        await using var context = CreateContext();
        await SeedSplitOrderAsync(context, withReturn: true, invoiceSyncedAfterReturn: true);
        var service = CreateService(context);

        var report = await service.GetReportAsync(new LegalEntityReconciliationQueryDto
        {
            FromDate = DateTime.Today.AddDays(-1),
            ToDate = DateTime.Today.AddDays(1),
            LegalEntityId = 2
        });

        report.OrderCount.Should().Be(1);
        report.DiscrepantOrderCount.Should().Be(0);
        report.EntitySummaries.Should().ContainSingle();
        var hkd2 = report.EntitySummaries.Single();
        hkd2.LegalEntityCode.Should().Be("HKD-2");
        hkd2.AllocationGrossTotal.Should().Be(180_000m);
        hkd2.ReversalTotal.Should().Be(80_000m);
        hkd2.AllocationNetTotal.Should().Be(100_000m);
        hkd2.ExpectedInvoiceTotal.Should().Be(100_000m);
        hkd2.ActualInvoiceTotal.Should().Be(100_000m);
        report.Orders.Items.Single().IsBalanced.Should().BeTrue();
    }

    [Fact]
    public async Task Discrepancy_filter_should_find_invoice_not_synced_after_return()
    {
        await using var context = CreateContext();
        var data = await SeedSplitOrderAsync(context, withReturn: true, invoiceSyncedAfterReturn: false);
        var service = CreateService(context);

        var report = await service.GetReportAsync(new LegalEntityReconciliationQueryDto
        {
            FromDate = DateTime.Today.AddDays(-1),
            ToDate = DateTime.Today.AddDays(1),
            OnlyDiscrepancies = true
        });

        report.OrderCount.Should().Be(1);
        report.DiscrepantOrderCount.Should().Be(1);
        var row = report.Orders.Items.Single();
        row.OrderId.Should().Be(data.Order.Id);
        row.OrderMatches.Should().BeTrue();
        row.InvoiceMatches.Should().BeFalse();
        row.InvoiceDifference.Should().Be(80_000m);
    }

    [Fact]
    public async Task Invoice_expectation_should_exclude_variant_without_input_invoice()
    {
        await using var context = CreateContext();
        var data = await SeedSplitOrderAsync(context, withReturn: false, invoiceSyncedAfterReturn: true);
        data.Variant.HasInputInvoice = false;
        foreach (var invoice in context.InvoiceHeads)
            invoice.GrandTotal = 0m;
        await context.SaveChangesAsync();
        var service = CreateService(context);

        var result = await service.GetOrderBreakdownAsync(data.Order.Id);

        result.Should().NotBeNull();
        result!.UnifiedOrderTotal.Should().Be(280_000m);
        result.AllocationGrossTotal.Should().Be(280_000m);
        result.ExpectedInvoiceTotal.Should().Be(0m);
        result.ActualInvoiceTotal.Should().Be(0m);
        result.IsBalanced.Should().BeTrue();
    }

    private static LegalEntityReconciliationService CreateService(InMemoryAppDbContext context)
        => new(new LegalEntityReconciliationRepository(context), new TestCurrentStore(1));

    private static async Task<SeededData> SeedSplitOrderAsync(
        InMemoryAppDbContext context,
        bool withReturn,
        bool invoiceSyncedAfterReturn)
    {
        context.Stores.Add(new Store
        {
            Id = 1,
            Name = "store-one",
            SubDomain = "store-one",
            SubDomainNormalized = "STORE-ONE",
            IsActive = true,
            IsMultiLegalEntityEnabled = true,
            MultiLegalEntityActivatedAtUtc = DateTime.UtcNow.AddDays(-1),
            RowVersion = new byte[8]
        });

        var first = NewLegalEntity(1, "HKD-1", 1);
        var second = NewLegalEntity(2, "HKD-2", 2);
        context.LegalEntities.AddRange(first, second);
        context.Warehouses.AddRange(
            NewWarehouse(11, first.Id, "KHO-1"),
            NewWarehouse(22, second.Id, "KHO-2"));

        var variant = new ProductVariant
        {
            StoreId = 1,
            ProductId = 1,
            Sku = "SKU-22-8",
            ProductVariantName = "Sản phẩm Phase 22.8",
            HasInputInvoice = true,
            IsActive = true,
            RowVersion = new byte[8]
        };
        context.ProductVariants.Add(variant);

        var order = new Order
        {
            StoreId = 1,
            OrderNumber = "UAT-22-8",
            POSShiftId = 1,
            Status = withReturn ? OrderStatus.Refunded : OrderStatus.Completed,
            PaymentStatus = PaymentStatus.Paid,
            Subtotal = 280_000m,
            GrandTotal = 280_000m,
            PaidTotal = 280_000m,
            CompletedAtUtc = DateTime.UtcNow,
            LegalEntityAllocatedAtUtc = DateTime.UtcNow,
            LegalEntityCount = 2,
            HasMultipleLegalEntities = true,
            RowVersion = new byte[8]
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var line = new OrderLine
        {
            StoreId = 1,
            OrderId = order.Id,
            ProductId = 1,
            VariantId = variant.Id,
            ItemName = "Sản phẩm Phase 22.8",
            Sku = variant.Sku,
            Barcode = "22080001",
            UnitName = "Cái",
            Quantity = 3m,
            BaseQuantity = 3m,
            Multiplier = 1m,
            UnitPrice = 100_000m,
            LineTotal = 280_000m,
            RowVersion = new byte[8]
        };
        context.OrderLines.Add(line);
        await context.SaveChangesAsync();

        var firstAllocation = NewAllocation(order.Id, line.Id, variant.Id, first.Id, 11, 1, 1m, 100_000m);
        var secondAllocation = NewAllocation(order.Id, line.Id, variant.Id, second.Id, 22, 2, 2m, 180_000m);
        context.OrderLegalEntityAllocations.AddRange(firstAllocation, secondAllocation);
        await context.SaveChangesAsync();

        context.InvoiceHeads.AddRange(
            NewInvoice(order.Id, first.Id, 100_000m),
            NewInvoice(order.Id, second.Id, withReturn && invoiceSyncedAfterReturn ? 100_000m : 180_000m));

        if (withReturn)
        {
            context.OrderLegalEntityAllocationReversals.Add(new OrderLegalEntityAllocationReversal
            {
                StoreId = 1,
                OrderId = order.Id,
                OrderLineId = line.Id,
                OrderLegalEntityAllocationId = secondAllocation.Id,
                LegalEntityId = second.Id,
                WarehouseId = 22,
                ProductVariantId = variant.Id,
                SourceValuationEntryId = 9001,
                ReversalType = OrderLegalEntityReversalType.ReturnRestock,
                BaseQuantity = 1m,
                FinancialAmount = 80_000m,
                OccurredAtUtc = DateTime.UtcNow,
                RowVersion = new byte[8]
            });
        }

        await context.SaveChangesAsync();
        return new SeededData(order, variant);
    }

    private static LegalEntity NewLegalEntity(int id, string code, int priority)
        => new()
        {
            Id = id,
            StoreId = 1,
            Code = code,
            Name = code,
            LegalName = $"Hộ kinh doanh {code}",
            SalePriority = priority,
            IsActive = true,
            RowVersion = new byte[8]
        };

    private static Warehouse NewWarehouse(int id, int legalEntityId, string code)
        => new()
        {
            Id = id,
            StoreId = 1,
            LegalEntityId = legalEntityId,
            Code = code,
            Name = $"Kho {code}",
            IsActive = true,
            RowVersion = new byte[8]
        };

    private static OrderLegalEntityAllocation NewAllocation(
        int orderId,
        int orderLineId,
        int variantId,
        int legalEntityId,
        int warehouseId,
        int priority,
        decimal quantity,
        decimal netAmount)
        => new()
        {
            StoreId = 1,
            OrderId = orderId,
            OrderLineId = orderLineId,
            ProductVariantId = variantId,
            LegalEntityId = legalEntityId,
            WarehouseId = warehouseId,
            SalePriority = priority,
            Quantity = quantity,
            BaseQuantity = quantity,
            UnitPrice = 100_000m,
            LineTotal = netAmount,
            NetAmount = netAmount,
            AllocationSource = OrderLegalEntityAllocationSource.AutoBySalePriority,
            RowVersion = new byte[8]
        };

    private static InvoiceHead NewInvoice(int orderId, int legalEntityId, decimal grandTotal)
        => new()
        {
            StoreId = 1,
            OrderId = orderId,
            LegalEntityId = legalEntityId,
            BuyerType = "NoInvoice",
            TotalQuantity = 1m,
            SubTotal = grandTotal,
            GrandTotal = grandTotal,
            ProviderStatus = InvoiceProviderStatus.LocalDraft,
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

    private sealed record SeededData(Order Order, ProductVariant Variant);

    private sealed class TestCurrentStore : ICurrentStore
    {
        public TestCurrentStore(int storeId) => StoreId = storeId;
        public int StoreId { get; }
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId => 99;
        public string? UserName => "phase-22-8-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
