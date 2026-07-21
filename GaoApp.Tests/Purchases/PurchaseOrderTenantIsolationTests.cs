using FluentAssertions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Purchases;

public sealed class PurchaseOrderTenantIsolationTests
{
    [Fact]
    public async Task Purchase_headers_and_lines_should_be_filtered_by_current_store()
    {
        var options = new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using (var seed = Create(options, t => t.SetHostAdmin()))
        {
            seed.PurchaseOrders.AddRange(NewOrder(1, "PO-S1", 11), NewOrder(2, "PO-S2", 22));
            await seed.SaveChangesAsync();
        }

        await using var storeOne = Create(options, t => t.SetStore(1, "store-one"));
        (await storeOne.PurchaseOrders.Select(x => x.OrderNumber).ToListAsync()).Should().Equal("PO-S1");
        (await storeOne.PurchaseOrderLines.Select(x => x.StoreId).ToListAsync()).Should().OnlyContain(x => x == 1);
    }

    [Fact]
    public async Task Purchase_update_guard_should_reject_cross_store_write()
    {
        var options = new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using (var seed = Create(options, t => t.SetHostAdmin()))
        {
            seed.PurchaseOrders.Add(NewOrder(2, "PO-S2", 22));
            await seed.SaveChangesAsync();
        }
        await using var storeOne = Create(options, t => t.SetStore(1, "store-one"));
        var order = await storeOne.PurchaseOrders.IgnoreQueryFilters().SingleAsync();
        order.Note = "cross store";
        var action = () => storeOne.SaveChangesAsync();
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("Tenant mismatch UPDATE");
    }

    [Fact]
    public void Purchase_model_should_enforce_store_scoped_numbers_payable_idempotency_and_line_tenant_fk()
    {
        var options = new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var context = Create(options, t => t.SetStore(1, "store-one"));

        var orderType = context.Model.FindEntityType(typeof(PurchaseOrder))!;
        orderType.GetIndexes().Single(x => x.Properties.Select(p => p.Name)
                .SequenceEqual(new[] { nameof(PurchaseOrder.StoreId), nameof(PurchaseOrder.OrderNumber) }))
            .IsUnique.Should().BeTrue();

        var payableType = context.Model.FindEntityType(typeof(PurchasePayable))!;
        payableType.GetIndexes().Single(x => x.Properties.Select(p => p.Name)
                .SequenceEqual(new[] { nameof(PurchasePayable.StoreId), nameof(PurchasePayable.SourceKey) }))
            .IsUnique.Should().BeTrue();

        var lineType = context.Model.FindEntityType(typeof(PurchaseOrderLine))!;
        lineType.GetForeignKeys().Should().Contain(x =>
            x.Properties.Single().Name == nameof(PurchaseOrderLine.StoreId) &&
            x.PrincipalEntityType.ClrType == typeof(Store));
    }

    private static InMemoryAppDbContext Create(
        DbContextOptions<InMemoryAppDbContext> options,
        Action<TenantContext> configure)
    {
        var tenant = new TenantContext();
        configure(tenant);
        var context = new InMemoryAppDbContext(options, tenant, new CurrentUser());
        context.VerifyRowVersionConfiguration();
        return context;
    }

    private static PurchaseOrder NewOrder(int storeId, string number, int suffix)
    {
        var order = new PurchaseOrder
        {
            StoreId = storeId, OrderNumber = number, SupplierId = suffix,
            ExpectedWarehouseId = suffix, LegalEntityId = suffix,
            OrderDate = DateTime.UtcNow, Status = PurchaseOrderStatus.Draft,
            RowVersion = new byte[8]
        };
        order.Lines.Add(new PurchaseOrderLine
        {
            StoreId = storeId, LineNo = 1, ProductVariantId = suffix, UnitId = suffix,
            ProductUnitConversionId = suffix, ProductNameSnapshot = number,
            UnitNameSnapshot = "unit", ConversionFactor = 1, OrderedQuantity = 1,
            RowVersion = new byte[8]
        });
        return order;
    }

    private sealed class CurrentUser : ICurrentUser
    {
        public int? UserId => 1;
        public string? UserName => "test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
