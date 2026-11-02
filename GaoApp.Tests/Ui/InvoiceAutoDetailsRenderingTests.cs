using System.Globalization;
using System.Text.Encodings.Web;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Domain.Enums;
using GaoApp.Web.Areas.Admin.Controllers;
using GaoApp.Web.Areas.Admin.ViewModels.Invoices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GaoApp.Tests.Ui;

public sealed class InvoiceAutoDetailsRenderingTests
{
    [Theory]
    [InlineData("vi-VN", true)]
    [InlineData("vi-VN", false)]
    [InlineData("en-US", true)]
    [InlineData("en-US", false)]
    public async Task Actual_Auto_partial_respects_edit_capability_encodes_values_and_keeps_provenance(string culture, bool canEdit)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var invoice = Invoice(canEdit);
            var line = invoice.Details[0];
            var html = await RenderAsync("_InvoiceAutoLinesTable", new InvoiceDetailViewModel { Invoice = invoice });

            Assert.Contains("data-manual-detail-id=\"51\"", html);
            Assert.DoesNotContain("data-manual-detail-id=\"52\"", html);
            Assert.Contains("data-manual-quantity=\"1.25\"", html);
            Assert.Contains("data-manual-unit-price=\"12345.67\"", html);
            Assert.Contains("data-manual-vat-rate=\"8.5\"", html);
            Assert.Contains(line.Quantity.ToString("N2", CultureInfo.CurrentCulture), html);
            Assert.Contains(line.VatAmount.ToString("N0", CultureInfo.CurrentCulture), html);
            Assert.Contains("data-manual-display=\"vatRate\"", html);
            Assert.Contains("data-manual-display=\"note\"", html);
            Assert.Contains("<td>#73</td>", html);
            Assert.Contains("#19", html);

            Assert.Contains(HtmlEncoder.Default.Encode(line.ItemName), html);
            Assert.Contains("data-manual-note=\"" + HtmlEncoder.Default.Encode(line.Note!) + "\"", html);
            Assert.DoesNotContain("<script>synthetic-item</script>", html);
            Assert.DoesNotContain(" onmouseover=\"synthetic()", html);
            Assert.Equal(canEdit, html.Contains("data-edit-manual-detail-id=\"51\"", StringComparison.Ordinal));
            Assert.Equal(canEdit, html.Contains("data-delete-manual-detail-id=\"51\"", StringComparison.Ordinal));
            Assert.Equal(canEdit, html.Contains("data-add-invoice-detail", StringComparison.Ordinal));
            Assert.Equal(!canEdit, html.Contains("manual-detail-row is-locked", StringComparison.Ordinal));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Actual_Manual_partial_uses_the_same_edit_capability_even_when_IsLocked_is_false(bool canEdit)
    {
        var invoice = Invoice(canEdit);
        Assert.False(invoice.IsLocked);
        var html = await RenderAsync("_ManualLinesTable", invoice);

        Assert.Contains("data-manual-detail-id=\"52\"", html);
        Assert.DoesNotContain("data-manual-detail-id=\"51\"", html);
        Assert.Equal(canEdit, html.Contains("data-delete-manual-detail-id=\"52\"", StringComparison.Ordinal));
        Assert.Equal(!canEdit, html.Contains("manual-detail-row is-locked", StringComparison.Ordinal));
        Assert.Contains("data-manual-note=\"" + HtmlEncoder.Default.Encode(invoice.Details[1].Note!) + "\"", html);
        Assert.DoesNotContain(" onmouseover=\"synthetic()", html);
    }

    private static InvoiceHeadDto Invoice(bool canEdit) => new()
    {
        Id = 41, CanEditDetails = canEdit, IsLocked = false, ProviderStatus = InvoiceProviderStatus.LocalDraft,
        Details =
        [
            Line(51, InvoiceDetailSourceType.FromOrderLine),
            Line(52, InvoiceDetailSourceType.Manual)
        ]
    };

    private static InvoiceDetailDto Line(int id, InvoiceDetailSourceType source) => new()
    {
        Id = id, InvoiceHeadId = 41, SourceType = source, ProductVariantId = 31,
        OrderLineId = source == InvoiceDetailSourceType.FromOrderLine ? 73 : null,
        OrderLegalEntityAllocationId = source == InvoiceDetailSourceType.FromOrderLine ? 19 : null,
        ItemName = "<script>synthetic-item</script> \"quoted\"", UnitName = "box",
        Quantity = 1.25m, UnitPrice = 12345.67m, VatRate = 8.5m, VatAmount = 1312m, TotalAmount = 16744m,
        Note = "\" onmouseover=\"synthetic() <b>note</b>"
    };

    private static async Task<string> RenderAsync<T>(string partial, T model)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ApplicationName = typeof(InvoiceController).Assembly.FullName,
            ContentRootPath = Path.GetTempPath()
        });
        builder.Logging.ClearProviders();
        builder.Services.AddControllersWithViews();
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        // Building supplies the compiled Razor engine. No host startup, listener, database or controller is run.
        await using var app = builder.Build();
        using var scope = app.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var route = new RouteData();
        route.Values["area"] = "Admin";
        route.Values["controller"] = "Invoice";
        route.Values["action"] = partial;
        var action = new ActionContext(http, route, new ActionDescriptor());
        var found = scope.ServiceProvider.GetRequiredService<IRazorViewEngine>()
            .GetView(null, "/Areas/Admin/Views/Invoice/" + partial + ".cshtml", isMainPage: false);
        Assert.True(found.Success, string.Join(Environment.NewLine, found.SearchedLocations ?? []));
        using var writer = new StringWriter(CultureInfo.CurrentCulture);
        var data = new ViewDataDictionary<T>(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model };
        await found.View.RenderAsync(new ViewContext(action, found.View, data,
            new TempDataDictionary(http, new MemoryTempData()), writer, new HtmlHelperOptions()));
        return writer.ToString();
    }

    private sealed class MemoryTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
