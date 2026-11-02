using System.Text.Json;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Application.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Invoices;
using GaoApp.Infrastructure.Repositories.LegalEntities;
using GaoApp.Infrastructure.Repositories.Orders;
using GaoApp.Infrastructure.Repositories.Products;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Invoices;

public sealed class InvoiceRevenueTaxTests
{
    [Theory]
    [InlineData(1, 2000)]
    [InlineData(2, 4000)]
    [InlineData(3, 6000)]
    [InlineData(5, 10000)]
    public async Task Saved_reduction_and_partner_payload_match_without_changing_lines_or_POS(int rate, int reduction)
    {
        await using var db = await CreateDb();
        var service = Service(db);
        var result = await service.UpdateRevenueTaxAsync(Request(rate));
        Assert.True(result.IsSuccess, result.Error.Message);
        Assert.Equal(1000000m, result.Value.SubTotal);
        Assert.Equal(reduction, result.Value.RevenueTaxReductionAmount);
        Assert.Equal(1000000m - reduction, result.Value.GrandTotal);
        Assert.Equal(1000000m, Assert.Single(result.Value.Details).TotalAmount);
        var payload = await Build(result.Value);
        Assert.True(payload.IsSuccess, payload.Error.Message);
        Assert.Equal(rate, payload.Value.Payload.GeneralInvoiceInfo.AdjustAmount20);
        using var json = JsonDocument.Parse(payload.Value.Json);
        Assert.Equal(rate, json.RootElement.GetProperty("generalInvoiceInfo").GetProperty("adjustAmount20").GetInt32());
        Assert.Equal(result.Value.GrandTotal, payload.Value.Payload.SummarizeInfo.TotalAmountWithTax);
        Assert.Equal(1000000, Assert.Single(payload.Value.Payload.ItemInfo).ItemTotalAmountWithoutTax);
        Assert.Empty(payload.Value.Warnings);
        var pos = await db.Orders.AsNoTracking().SingleAsync();
        Assert.Equal(1000000m, pos.GrandTotal);
        Assert.Equal(InvoiceIssuanceRoute.Manual, pos.InvoiceIssuanceRoute);
        Assert.Empty(await db.OrderPayments.ToListAsync());
    }

    [Fact]
    public async Task Editing_line_recalculates_reduction_and_disabling_restores_original_amount_and_omits_API_flag()
    {
        await using var db = await CreateDb();
        var service = Service(db);
        Assert.True((await service.UpdateRevenueTaxAsync(Request(1))).IsSuccess);
        var updated = await service.UpdateManualDetailAsync(new() { InvoiceDetailId = 81, Quantity = 2, UnitPrice = 1000000, VatRate = 0 });
        Assert.True(updated.IsSuccess, updated.Error.Message);
        Assert.Equal(4000, updated.Value.RevenueTaxReductionAmount);
        Assert.Equal(1996000, updated.Value.GrandTotal);
        var vat = await service.UpdateManualDetailAsync(new() { InvoiceDetailId = 81, Quantity = 2, UnitPrice = 1000000, VatRate = 8 });
        Assert.False(vat.IsSuccess);
        Assert.Equal("Invoice.RevenueTaxWithVat", vat.Error.Code);
        var disabled = await service.UpdateRevenueTaxAsync(Request(null));
        Assert.True(disabled.IsSuccess, disabled.Error.Message);
        Assert.Null(disabled.Value.RevenueTaxPercentage);
        Assert.Equal(0, disabled.Value.RevenueTaxReductionAmount);
        Assert.Equal(2000000, disabled.Value.GrandTotal);
        var payload = await Build(disabled.Value);
        Assert.True(payload.IsSuccess, payload.Error.Message);
        Assert.DoesNotContain("adjustAmount20", payload.Value.Json);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(20)]
    [InlineData(-1)]
    public async Task Unsupported_rate_is_rejected_without_changing_invoice(int rate)
    {
        await using var db = await CreateDb();
        var result = await Service(db).UpdateRevenueTaxAsync(Request(rate));
        Assert.False(result.IsSuccess);
        Assert.Null((await db.InvoiceHeads.SingleAsync()).RevenueTaxPercentage);
        Assert.Equal(1000000, (await db.InvoiceHeads.SingleAsync()).GrandTotal);
    }

    [Theory]
    [InlineData(InvoiceProviderStatus.Issued)]
    [InlineData(InvoiceProviderStatus.Issuing)]
    [InlineData(InvoiceProviderStatus.IssuedWaitingNumber)]
    public async Task Submitted_invoices_cannot_change_reduction(InvoiceProviderStatus status)
    {
        await using var db = await CreateDb();
        var head = await db.InvoiceHeads.SingleAsync(); head.ProviderStatus = status; await db.SaveChangesAsync();
        Assert.False((await Service(db).UpdateRevenueTaxAsync(Request(1))).IsSuccess);
        Assert.Null(head.RevenueTaxPercentage);
    }

    [Fact]
    public async Task Locked_automatic_and_stale_version_are_rejected()
    {
        await using var db = await CreateDb(); var service = Service(db);
        Assert.False((await service.UpdateRevenueTaxAsync(Request(1) with { ExpectedVersion = "invalid" })).IsSuccess);
        var head = await db.InvoiceHeads.SingleAsync(); head.IsLocked = true; await db.SaveChangesAsync();
        Assert.False((await service.UpdateRevenueTaxAsync(Request(1))).IsSuccess);
        head.IsLocked = false; await db.SaveChangesAsync();
        var order = await db.Orders.SingleAsync(); order.InvoiceIssuanceRoute = InvoiceIssuanceRoute.Automatic; await db.SaveChangesAsync();
        Assert.Equal("Invoice.RevenueTaxManualOnly", (await service.UpdateRevenueTaxAsync(Request(1))).Error.Code);
    }

    [Theory]
    [InlineData("1", 2026, 10, 30, false)]
    [InlineData("2", 2025, 6, 30, false)]
    [InlineData("2", 2027, 1, 1, false)]
    [InlineData("2", 2026, 10, 30, true)]
    public void Wrong_invoice_type_period_or_VAT_is_rejected(string type, int y, int m, int d, bool vat)
        => Assert.NotNull(InvoiceRevenueTaxPolicy.Validate(1, type, null, new(y, m, d), vat));

    [Fact]
    public async Task Stale_saved_totals_cannot_be_sent_to_provider()
    {
        await using var db = await CreateDb();
        var saved = (await Service(db).UpdateRevenueTaxAsync(Request(1))).Value;
        saved.RevenueTaxReductionAmount = 20000;
        var result = await Build(saved);
        Assert.False(result.IsSuccess); Assert.Equal("Invoice.RevenueTaxTotalsChanged", result.Error.Code);
    }

    [Fact]
    public async Task Manual_replacement_can_choose_reduction_when_original_POS_route_was_automatic()
    {
        await using var db = await CreateDb();
        var head = await db.InvoiceHeads.SingleAsync(); head.CorrectionType = InvoiceCorrectionType.Replacement;
        var order = await db.Orders.SingleAsync(); order.InvoiceIssuanceRoute = InvoiceIssuanceRoute.Automatic;
        await db.SaveChangesAsync();
        var result = await Service(db).UpdateRevenueTaxAsync(Request(1));
        Assert.True(result.IsSuccess, result.Error.Message); Assert.Equal(998000, result.Value.GrandTotal);
    }

    private static UpdateInvoiceRevenueTaxRequest Request(int? rate) => new()
        { InvoiceHeadId = 71, Enabled = rate.HasValue, Percentage = rate, ExpectedVersion = Convert.ToBase64String(new byte[8]) };

    private static async Task<InMemoryAppDbContext> CreateDb()
    {
        var tenant = new TenantContext(); tenant.SetStore(1, "tax-test");
        var db = new InMemoryAppDbContext(new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options, tenant, new CurrentUser());
        db.Stores.Add(new Store { Id = 1, Name = "Tax test", SubDomain = "tax-test", SubDomainNormalized = "TAX-TEST", IsActive = true });
        db.InvoiceProviderSettings.Add(Setting());
        db.Orders.Add(new Order { Id = 41, StoreId = 1, OrderNumber = "TAX-TEST", POSShiftId = 1,
            Status = OrderStatus.Completed, GrandTotal = 1000000, InvoiceIssuanceRoute = InvoiceIssuanceRoute.Manual });
        db.InvoiceHeads.Add(new InvoiceHead { Id = 71, StoreId = 1, OrderId = 41, InvoiceProviderSettingId = 11,
            InvoiceType = "2", TemplateCode = "2/001", InvoiceSeries = "C26MTT", SupplierTaxCode = "0100000001",
            InvoiceDate = new(2026, 10, 30), RowVersion = new byte[8], TransactionUuid = Guid.NewGuid().ToString("D"),
            SubTotal = 1000000, GrandTotal = 1000000, TotalQuantity = 1,
            Details = [new InvoiceDetail { Id = 81, StoreId = 1, SourceType = InvoiceDetailSourceType.Manual, ItemName = "Hàng đủ điều kiện", Quantity = 1,
                UnitPrice = 1000000, Amount = 1000000, TotalAmount = 1000000 }] });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear(); return db;
    }

    private static InvoiceProviderSetting Setting() => new() { Id = 11, StoreId = 1, ProviderCode = "VIETTEL", BaseUrl = "https://example.test",
        Username = "test", Password = "synthetic-protected", SupplierTaxCode = "0100000001", InvoiceType = "2", TemplateCode = "2/001",
        InvoiceSeries = "C26MTT", CurrencyCode = "VND", IsActive = true };

    private static InvoiceCommandService Service(InMemoryAppDbContext db) => new(new InvoiceRepository(db), new ProductVariantRepository(db),
        new InvoiceProviderSettingRepository(db, new Protector()), new OrderLegalEntityAllocationRepository(db),
        new LegalEntityRepository(db), new InvoiceInputStockRepository(db));

    private static Task<Result<ViettelInvoicePayloadResultDto>> Build(InvoiceHeadDto invoice) => new ViettelInvoicePayloadBuilder(
        InvoiceCallProxy.For<IInvoiceService>((method, _) => method.Name == nameof(IInvoiceService.GetInvoiceDetailAsync)
            ? Task.FromResult(Result<InvoiceHeadDto>.Success(invoice)) : throw new InvalidOperationException(method.Name)),
        InvoiceCallProxy.For<IInvoiceProviderSettingRepository>((_, _) => Task.FromResult<InvoiceProviderSetting?>(Setting())),
        LegacyInvoiceConfigurationTests.EmptyLogs()).BuildAsync(invoice.Id);

    private sealed class Protector : IInvoiceProviderCredentialProtector
    {
        public bool IsProtected(string value) => true;
        public string Protect(string value) => value;
        public string Unprotect(string value) => value;
    }
    private sealed class CurrentUser : ICurrentUser
    {
        public int? UserId => 99;
        public string? UserName => "tax-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
