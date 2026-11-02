using System.Security.Cryptography;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Domain.Constants;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Invoices;

public sealed partial class AutoInvoiceServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Manual_legacy_draft_uses_current_provider_without_rewriting_historical_configuration(bool missingSetting)
    {
        var repo = CreateRepository(false);
        var issue = new FakeIssueService();
        var invoice = ManualLegacyDraft();
        if (!missingSetting)
        {
            invoice.InvoiceProviderSettingId = 17;
            invoice.InvoiceProviderSetting = LegacyInvoiceConfigurationTests.Setting(17);
            invoice.InvoiceProviderSetting.IsActive = false;
        }
        repo.Candidates.Add(invoice);
        var setting = LegacyInvoiceConfigurationTests.Setting();
        var service = CreateService(repo, issue, providerSettings: CurrentProvider(setting));

        var result = await service.IssueManualAsync(invoice.Id);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(1, issue.Calls);
        Assert.True(Assert.Single(repo.Operations).IsManual);
        Assert.Equal(missingSetting ? null : (int?)17, invoice.InvoiceProviderSettingId);
        if (!missingSetting) Assert.False(invoice.InvoiceProviderSetting!.IsActive);
    }

    [Fact]
    public async Task Manual_legacy_shortage_reports_exact_product_quantity_unit_and_warehouse_before_claim_or_provider_call()
    {
        var repo = CreateRepository(false);
        var issue = new FakeIssueService();
        var invoice = ManualLegacyDraft();
        repo.Candidates.Add(invoice);
        var stock = new FixedManualStock(new InvoiceInputStockAvailabilityDto
        {
            InvoiceHeadId = invoice.Id,
            Lines =
            [
                new() { ProductVariantId = 434853, WarehouseId = 1, ItemName = "Bánh sầu riêng",
                    RequiredBaseQuantity = 100m, EligibleInboundBaseQuantity = 95m },
                new() { ProductVariantId = 445033, WarehouseId = 1, ItemName = "Bánh thập cẩm",
                    RequiredBaseQuantity = 100m, EligibleInboundBaseQuantity = 41m }
            ],
            DetailLines =
            [
                new() { ProductVariantId = 434853, WarehouseId = 1, UnitFactor = 1m, UnitName = "cái", WarehouseName = "Kho chính" },
                new() { ProductVariantId = 445033, WarehouseId = 1, UnitFactor = 1m, UnitName = "cái", WarehouseName = "Kho chính" }
            ]
        });
        var service = CreateService(repo, issue, stock, providerSettings:
            CurrentProvider(LegacyInvoiceConfigurationTests.Setting()));

        var result = await service.IssueManualAsync(invoice.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("Invoice.InputInvoiceStockInsufficient", result.Error!.Code);
        Assert.Contains("Bánh sầu riêng: cần 100 cái, có 95 cái, thiếu 5 cái tại Kho chính", result.Error.Message);
        Assert.Contains("Bánh thập cẩm: cần 100 cái, có 41 cái, thiếu 59 cái tại Kho chính", result.Error.Message);
        Assert.Equal(0, issue.Calls);
        Assert.Empty(repo.Operations);
        Assert.Null(invoice.InvoiceProviderSettingId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Manual_legacy_draft_still_rejects_missing_or_foreign_current_provider(int providerStore)
    {
        var repo = CreateRepository(false);
        var issue = new FakeIssueService();
        var invoice = ManualLegacyDraft();
        repo.Candidates.Add(invoice);
        var setting = LegacyInvoiceConfigurationTests.Setting();
        setting.StoreId = providerStore;
        var service = CreateService(repo, issue, providerSettings:
            CurrentProvider(providerStore == 0 ? null : setting));

        var result = await service.IssueManualAsync(invoice.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("InvoiceProvider.NotConfigured", result.Error!.Code);
        Assert.Equal(0, issue.Calls);
        Assert.Empty(repo.Operations);
    }

    [Fact]
    public async Task Manual_legacy_credential_failure_is_not_reported_as_missing_stock_or_missing_setting()
    {
        var repo = CreateRepository(false);
        var issue = new FakeIssueService();
        var invoice = ManualLegacyDraft();
        repo.Candidates.Add(invoice);
        var provider = InvoiceCallProxy.For<IInvoiceProviderSettingRepository>((_, _) =>
            throw new CryptographicException("Synthetic unavailable key"));
        var service = CreateService(repo, issue, providerSettings: provider);

        var result = await service.IssueManualAsync(invoice.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("InvoiceProvider.CredentialKeyUnavailable", result.Error!.Code);
        Assert.Equal(0, issue.Calls);
        Assert.Empty(repo.Operations);
    }

    [Fact]
    public async Task Ordinary_manual_invoice_without_a_setting_reports_the_actual_configuration_problem()
    {
        var repo = CreateRepository(false);
        var issue = new FakeIssueService();
        var invoice = ManualLegacyDraft();
        invoice.LegacySourceId = null;
        repo.Candidates.Add(invoice);
        var service = CreateService(repo, issue, providerSettings:
            InvoiceCallProxy.For<IInvoiceProviderSettingRepository>((_, _) =>
                throw new InvalidOperationException("An ordinary invoice must retain its configuration snapshot")));

        var result = await service.IssueManualAsync(invoice.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("InvoiceProvider.NotConfigured", result.Error!.Code);
        Assert.Contains("chưa gắn cấu hình Viettel", result.Error.Message);
        Assert.Equal(0, issue.Calls);
        Assert.Empty(repo.Operations);
    }

    private static InvoiceHead ManualLegacyDraft()
    {
        var invoice = Invoice(123399, DateTime.UtcNow.AddHours(-1));
        invoice.LegacySourceId = 1831908;
        invoice.ProviderStatus = InvoiceProviderStatus.LocalDraft;
        invoice.InvoiceProviderSettingId = null;
        invoice.InvoiceProviderSetting = null;
        invoice.Order!.InvoiceIssuanceRoute = InvoiceIssuanceRoute.Manual;
        invoice.BuyerType = InvoiceBuyerTypes.Individual;
        invoice.BuyerName = "Khách hàng thử nghiệm";
        invoice.BuyerAddress = "Địa chỉ thử nghiệm";
        return invoice;
    }

    private static IInvoiceProviderSettingRepository CurrentProvider(InvoiceProviderSetting? setting)
        => InvoiceCallProxy.For<IInvoiceProviderSettingRepository>((method, args) =>
        {
            Assert.Equal(nameof(IInvoiceProviderSettingRepository.GetActiveViettelAsync), method.Name);
            Assert.Equal(1, (int)args![0]!);
            return Task.FromResult(setting);
        });

    private sealed class FixedManualStock(InvoiceInputStockAvailabilityDto availability) : IInvoiceInputStockRepository
    {
        public Task LockStoreForIssueAsync(int storeId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<InvoiceInputStockAvailabilityDto> GetAvailabilityAsync(int invoiceHeadId, CancellationToken ct = default)
            => Task.FromResult(availability);
    }
}
