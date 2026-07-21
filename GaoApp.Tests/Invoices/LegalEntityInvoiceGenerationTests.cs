using FluentAssertions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Application.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Repositories.Invoices;
using GaoApp.Infrastructure.Repositories.LegalEntities;
using GaoApp.Infrastructure.Repositories.Orders;
using GaoApp.Infrastructure.Repositories.Products;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Invoices;

public sealed class LegalEntityInvoiceGenerationTests
{
    [Fact]
    public async Task One_legal_entity_should_create_one_head_with_its_setting_and_allocation()
    {
        await using var context = CreateContext();
        var data = await SeedOrderAsync(context, splitAcrossTwoEntities: false);
        var service = CreateService(context);

        var result = await service.GenerateInvoicesFromOrderAsync(data.Order.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        var invoice = result.Value.Single();
        invoice.LegalEntityId.Should().Be(data.FirstLegalEntity.Id);
        invoice.InvoiceProviderSettingId.Should().Be(data.FirstSetting.Id);
        invoice.SupplierTaxCode.Should().Be(data.FirstLegalEntity.TaxCode);
        invoice.Details.Should().ContainSingle();
        invoice.Details.Single().OrderLegalEntityAllocationId
            .Should().Be(data.Allocations[0].Id);
        invoice.GrandTotal.Should().Be(data.Allocations[0].NetAmount);
    }

    [Fact]
    public async Task One_second_legal_entity_should_create_only_its_head_and_setting()
    {
        await using var context = CreateContext();
        var data = await SeedOrderAsync(context, splitAcrossTwoEntities: true);
        data.Allocations[0].IsDeleted = true;
        await context.SaveChangesAsync();
        var service = CreateService(context);

        var result = await service.GenerateInvoicesFromOrderAsync(data.Order.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value.Single().LegalEntityId.Should().Be(data.SecondLegalEntity.Id);
        result.Value.Single().InvoiceProviderSettingId.Should().Be(data.SecondSetting.Id);
        result.Value.Single().SupplierTaxCode.Should().Be(data.SecondLegalEntity.TaxCode);
        result.Value.Single().Details.Single().OrderLegalEntityAllocationId
            .Should().Be(data.Allocations[1].Id);
    }

    [Fact]
    public async Task Two_legal_entities_should_create_two_heads_with_disjoint_lines_and_exact_totals()
    {
        await using var context = CreateContext();
        var data = await SeedOrderAsync(context, splitAcrossTwoEntities: true);
        var service = CreateService(context);

        var result = await service.GenerateInvoicesFromOrderAsync(data.Order.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Select(x => x.LegalEntityId)
            .Should().Equal(data.FirstLegalEntity.Id, data.SecondLegalEntity.Id);
        result.Value.Select(x => x.InvoiceProviderSettingId)
            .Should().Equal(data.FirstSetting.Id, data.SecondSetting.Id);
        result.Value.SelectMany(x => x.Details)
            .Select(x => x.OrderLegalEntityAllocationId)
            .Should().BeEquivalentTo(data.Allocations.Select(x => (int?)x.Id));
        result.Value.Sum(x => x.GrandTotal)
            .Should().Be(data.Allocations.Sum(x => x.NetAmount));
        result.Value[0].GrandTotal.Should().Be(data.Allocations[0].NetAmount);
        result.Value[1].GrandTotal.Should().Be(data.Allocations[1].NetAmount);
    }

    [Fact]
    public async Task Generation_should_keep_current_has_input_invoice_filter()
    {
        await using var context = CreateContext();
        var data = await SeedOrderAsync(context, splitAcrossTwoEntities: true);
        data.Variant.HasInputInvoice = false;
        await context.SaveChangesAsync();
        var service = CreateService(context);

        var result = await service.GenerateInvoicesFromOrderAsync(data.Order.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Should().OnlyContain(x => x.Details.Count == 0);
        result.Value.Should().OnlyContain(x => x.GrandTotal == 0m);
    }

    [Fact]
    public async Task Generation_should_be_idempotent_without_duplicate_heads_or_details()
    {
        await using var context = CreateContext();
        var data = await SeedOrderAsync(context, splitAcrossTwoEntities: true);
        var service = CreateService(context);

        var first = await service.GenerateInvoicesFromOrderAsync(data.Order.Id);
        var second = await service.GenerateInvoicesFromOrderAsync(data.Order.Id);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        second.Value.Select(x => x.Id).Should().Equal(first.Value.Select(x => x.Id));
        var headCount = await context.InvoiceHeads.CountAsync(x => x.OrderId == data.Order.Id);
        var detailCount = await context.InvoiceDetails.CountAsync();
        headCount.Should().Be(2);
        detailCount.Should().Be(2);
    }

    [Fact]
    public async Task Viettel_list_sync_should_call_each_active_legal_entity_setting()
    {
        await using var context = CreateContext();
        await SeedOrderAsync(context, splitAcrossTwoEntities: true);
        var client = new RecordingViettelListClient();
        var service = new ViettelInvoiceListSyncService(
            new InvoiceProviderSettingRepository(context, new TestCredentialProtector()),
            new InvoiceRepository(context),
            new InvoiceIntegrationLogRepository(context),
            client,
            new LegalEntityRepository(context));

        var result = await service.SyncAsync(new ViettelInvoiceListSyncRequestDto
        {
            FromDate = DateTime.Today,
            ToDate = DateTime.Today,
            UpdateLocalInvoices = false
        });

        result.IsSuccess.Should().BeTrue();
        client.SupplierTaxCodes.Should().Equal("0100000001", "0100000002");
        result.Value.Messages.Should().Contain(x => x.Contains("2 cấu hình Viettel"));
    }

    private static IInvoiceCommandService CreateService(InMemoryAppDbContext context)
        => new InvoiceCommandService(
            new InvoiceRepository(context),
            new ProductVariantRepository(context),
            new InvoiceProviderSettingRepository(context, new TestCredentialProtector()),
            new OrderLegalEntityAllocationRepository(context),
            new LegalEntityRepository(context));

    private static async Task<SeededData> SeedOrderAsync(
        InMemoryAppDbContext context,
        bool splitAcrossTwoEntities)
    {
        var store = new Store
        {
            Id = 1,
            Name = "store-one",
            SubDomain = "store-one",
            SubDomainNormalized = "STORE-ONE",
            IsActive = true,
            RowVersion = new byte[8]
        };
        context.Stores.Add(store);

        var firstSetting = NewSetting("0100000001", "C26AAA");
        var secondSetting = NewSetting("0100000002", "C26AAB");
        context.InvoiceProviderSettings.AddRange(firstSetting, secondSetting);
        await context.SaveChangesAsync();

        var firstLegalEntity = NewLegalEntity(
            "HKD-1",
            "0100000001",
            priority: 1,
            firstSetting.Id);
        var secondLegalEntity = NewLegalEntity(
            "HKD-2",
            "0100000002",
            priority: 2,
            secondSetting.Id);
        context.LegalEntities.AddRange(firstLegalEntity, secondLegalEntity);

        var variant = new ProductVariant
        {
            StoreId = 1,
            ProductId = 1,
            Sku = "SKU-22-7",
            ProductVariantName = "Sản phẩm Phase 22.7",
            HasInputInvoice = true,
            IsActive = true,
            RowVersion = new byte[8]
        };
        context.ProductVariants.Add(variant);

        var order = new Order
        {
            StoreId = 1,
            OrderNumber = "UAT-22-7",
            POSShiftId = 1,
            Subtotal = 280_000m,
            GrandTotal = splitAcrossTwoEntities ? 280_000m : 100_000m,
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
            ItemName = "Sản phẩm Phase 22.7",
            UnitName = "Cái",
            Quantity = splitAcrossTwoEntities ? 3m : 1m,
            BaseQuantity = splitAcrossTwoEntities ? 3m : 1m,
            Multiplier = 1m,
            UnitPrice = 100_000m,
            LineTotal = splitAcrossTwoEntities ? 280_000m : 100_000m,
            RowVersion = new byte[8]
        };
        context.OrderLines.Add(line);
        await context.SaveChangesAsync();

        var allocations = new List<OrderLegalEntityAllocation>
        {
            NewAllocation(order.Id, line.Id, variant.Id, firstLegalEntity.Id, 11, 1, 1m, 100_000m)
        };
        if (splitAcrossTwoEntities)
        {
            allocations.Add(NewAllocation(
                order.Id,
                line.Id,
                variant.Id,
                secondLegalEntity.Id,
                22,
                2,
                2m,
                180_000m));
        }

        context.OrderLegalEntityAllocations.AddRange(allocations);
        await context.SaveChangesAsync();

        return new SeededData(
            order,
            variant,
            firstLegalEntity,
            secondLegalEntity,
            firstSetting,
            secondSetting,
            allocations);
    }

    private static LegalEntity NewLegalEntity(
        string code,
        string taxCode,
        int priority,
        int settingId)
        => new()
        {
            StoreId = 1,
            Code = code,
            Name = code,
            LegalName = $"Hộ kinh doanh {code}",
            TaxCode = taxCode,
            Address = $"Địa chỉ {code}",
            SalePriority = priority,
            InvoiceProviderSettingId = settingId,
            IsActive = true,
            RowVersion = new byte[8]
        };

    private static InvoiceProviderSetting NewSetting(string taxCode, string series)
        => new()
        {
            StoreId = 1,
            ProviderCode = "VIETTEL",
            BaseUrl = "https://example.test",
            Username = "test-user",
            Password = "protected-test-password",
            SupplierTaxCode = taxCode,
            InvoiceType = "1",
            TemplateCode = "1/001",
            InvoiceSeries = series,
            CurrencyCode = "VND",
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

    private sealed record SeededData(
        Order Order,
        ProductVariant Variant,
        LegalEntity FirstLegalEntity,
        LegalEntity SecondLegalEntity,
        InvoiceProviderSetting FirstSetting,
        InvoiceProviderSetting SecondSetting,
        List<OrderLegalEntityAllocation> Allocations);

    private sealed class TestCredentialProtector : IInvoiceProviderCredentialProtector
    {
        public bool IsProtected(string value) => true;
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string protectedOrLegacyPlaintext) => protectedOrLegacyPlaintext;
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId => 99;
        public string? UserName => "phase-22-7-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }

    private sealed class RecordingViettelListClient : IViettelInvoiceListClient
    {
        public List<string> SupplierTaxCodes { get; } = new();

        public Task<Result<ViettelInvoiceListSyncResultDto>> GetInvoicesAsync(
            string baseUrl,
            string username,
            string password,
            GaoApp.Domain.Enums.InvoiceProviderAuthMode authMode,
            string supplierTaxCode,
            string invoiceType,
            string templateCode,
            string invoiceSeries,
            DateTime fromDate,
            DateTime toDate,
            int pageSize = 100,
            CancellationToken ct = default)
        {
            SupplierTaxCodes.Add(supplierTaxCode);
            return Task.FromResult(Result<ViettelInvoiceListSyncResultDto>.Success(new()
            {
                FromDate = fromDate,
                ToDate = toDate
            }));
        }
    }
}
