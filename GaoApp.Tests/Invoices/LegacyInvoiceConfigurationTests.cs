using System.Reflection;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Application.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Invoices;

public class LegacyInvoiceConfigurationTests
{
    internal static InvoiceProviderSetting Setting(int id = 701) => new()
    {
        Id = id, StoreId = 1, IsActive = true, ProviderCode = "VIETTEL",
        SupplierTaxCode = "NEW-TAX", InvoiceType = "2", TemplateCode = "2/LKD3",
        InvoiceSeries = "C26MTM", BaseUrl = "https://example.test", Username = "test",
        Password = "test", CurrencyCode = "VND", RowVersion = new byte[8]
    };

    private static InvoiceHeadDto Draft() => new()
    {
        Id = 104427, StoreId = 1, LegacySourceId = 1799542, InvoiceProviderSettingId = 12,
        SupplierTaxCode = "OLD-TAX", InvoiceType = "1", TemplateCode = "OLD-TEMPLATE",
        InvoiceSeries = "OLD-SERIES", GrandTotal = 100m, SubTotal = 100m,
        Details = [new() { Id = 1, ItemName = "Test product", Quantity = 1, UnitPrice = 100, Amount = 100, TotalAmount = 100 }]
    };

    internal static IInvoiceIntegrationLogRepository EmptyLogs() => InvoiceCallProxy.For<IInvoiceIntegrationLogRepository>(
        (method, _) => method.Name == nameof(IInvoiceIntegrationLogRepository.GetLatestByInvoiceHeadAsync)
            ? Task.FromResult(new List<InvoiceIntegrationLog>()) : throw new InvalidOperationException(method.Name));

    private static ViettelInvoicePayloadBuilder Builder(Func<InvoiceHeadDto> invoice, Func<int, int?, InvoiceProviderSetting?> resolve)
        => new(InvoiceCallProxy.For<IInvoiceService>((method, _) =>
                method.Name == nameof(IInvoiceService.GetInvoiceDetailAsync)
                    ? Task.FromResult(Result<InvoiceHeadDto>.Success(invoice())) : throw new InvalidOperationException(method.Name)),
            InvoiceCallProxy.For<IInvoiceProviderSettingRepository>((method, args) =>
                method.Name == nameof(IInvoiceProviderSettingRepository.GetForInvoiceAsync)
                    ? Task.FromResult(resolve((int)args![0]!, (int?)args[1])) : throw new InvalidOperationException(method.Name)), EmptyLogs());

    [Theory]
    [InlineData(null)]
    [InlineData(12)]
    public async Task Draft_uses_current_setting_even_with_old_mapping_and_missing_uuid(int? oldSetting)
    {
        var builder = Builder(() => { var d = Draft(); d.InvoiceProviderSettingId = oldSetting; return d; }, (store, id) =>
        { Assert.Equal(1, store); Assert.Null(id); return Setting(); });
        var result = await builder.BuildAsync(104427);
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("NEW-TAX", result.Value.SupplierTaxCode);
        Assert.Equal("2/LKD3", result.Value.TemplateCode);
        Assert.Equal("C26MTM", result.Value.InvoiceSeries);
        Assert.Equal("2", result.Value.Payload.GeneralInvoiceInfo!.InvoiceType);
        Assert.Equal(701, result.Value.InvoiceProviderSettingId);
        Assert.True(Guid.TryParse(result.Value.TransactionUuid, out _));
        Assert.False(result.Value.CanSyncByUuid);
        Assert.True(result.Value.CanIssue);
    }

    [Fact]
    public async Task Next_preview_uses_changed_active_configuration()
    {
        var setting = Setting();
        var builder = Builder(Draft, (_, _) => setting);
        Assert.Equal("C26MTM", (await builder.BuildAsync(104427)).Value.InvoiceSeries);
        setting = Setting(702); setting.SupplierTaxCode = "CHANGED-TAX"; setting.InvoiceSeries = "CHANGED-SERIES";
        var result = await builder.BuildAsync(104427);
        Assert.Equal("CHANGED-TAX", result.Value.SupplierTaxCode);
        Assert.Equal("CHANGED-SERIES", result.Value.InvoiceSeries);
        Assert.Equal(702, result.Value.InvoiceProviderSettingId);
    }

    [Fact]
    public async Task Issue_payload_uses_captured_setting_and_uuid_without_rereading_settings()
    {
        var uuid = Guid.NewGuid().ToString("D");
        var builder = Builder(Draft, (_, _) => throw new InvalidOperationException("Must use captured setting"));
        var result = await builder.BuildForIssueAsync(104427, Setting(702), uuid);
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(uuid, result.Value.Payload.GeneralInvoiceInfo!.TransactionUuid);
        Assert.Equal(702, result.Value.InvoiceProviderSettingId);
        Assert.Equal("NEW-TAX", result.Value.SupplierTaxCode);
    }

    [Theory]
    [InlineData(InvoiceProviderStatus.Issued)]
    [InlineData(InvoiceProviderStatus.IssuedWaitingNumber)]
    [InlineData(InvoiceProviderStatus.Issuing)]
    [InlineData(InvoiceProviderStatus.IssueFailed)]
    public async Task Submitted_invoice_retains_recorded_identity(InvoiceProviderStatus status)
    {
        var builder = Builder(() => { var d = Draft(); d.ProviderStatus = status; d.TransactionUuid = Guid.NewGuid().ToString("D"); return d; },
            (_, id) => { Assert.Equal(12, id); return Setting(12); });
        var result = await builder.BuildAsync(104427);
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("OLD-TAX", result.Value.SupplierTaxCode);
        Assert.Equal("OLD-TEMPLATE", result.Value.TemplateCode);
        Assert.Equal("OLD-SERIES", result.Value.InvoiceSeries);
    }

    [Fact]
    public async Task Archive_remains_read_only()
    {
        var builder = Builder(() => { var d = Draft(); d.LegacyReadOnly = true; return d; },
            (_, _) => throw new InvalidOperationException("Archive must not load credentials"));
        var result = await builder.BuildAsync(104427);
        Assert.False(result.IsSuccess);
        Assert.Equal("Invoice.LegacyNeedsReview", result.Error!.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Missing_or_other_store_configuration_is_rejected(int store)
    {
        var setting = Setting(); setting.StoreId = store;
        var result = await Builder(Draft, (_, _) => store == 0 ? null : setting).BuildAsync(104427);
        Assert.False(result.IsSuccess);
        Assert.Equal("InvoiceProvider.NotConfigured", result.Error!.Code);
    }
}

public class InvoiceCallProxy : DispatchProxy
{
    private Func<MethodInfo, object?[]?, object?> _handler = null!;
    public static T For<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = Create<T, InvoiceCallProxy>();
        ((InvoiceCallProxy)(object)proxy)._handler = handler;
        return proxy;
    }
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => _handler(targetMethod!, args);
}
