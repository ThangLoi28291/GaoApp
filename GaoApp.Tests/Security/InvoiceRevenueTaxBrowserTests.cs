using System.Diagnostics;
using System.Net;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class InvoiceRevenueTaxBrowserTests
{
    [Fact]
    public async Task Manual_tax_reduction_roundtrip_is_scoped_versioned_and_usable_on_desktop_and_phone()
    {
        await using var app = await FullApplicationFixture.StartWithAuditPausedAsync();
        var store = app.Stores[0]; var manager = await app.AddAccountAsync(store, "*");
        var denied = await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View);
        var other = await app.AddAccountAsync(app.Stores[1], "*");
        var orderId = await InvoiceOrderDraftSqlServerTests.SeedOrder(app, manager, "TAX-REDUCTION-UI", InvoiceIssuanceRoute.Manual);
        int id, lineId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var setting = await db.InvoiceProviderSettings.SingleAsync();
            setting.InvoiceType = "2"; setting.TemplateCode = "2/001"; setting.InvoiceSeries = "C26MTT"; setting.CurrencyCode = "VND";
            var head = new InvoiceHead { StoreId = store.StoreId, OrderId = orderId, InvoiceProviderSettingId = setting.Id,
                InvoiceType = "2", TemplateCode = setting.TemplateCode, InvoiceSeries = setting.InvoiceSeries,
                SupplierTaxCode = setting.SupplierTaxCode, TransactionUuid = Guid.NewGuid().ToString("D"),
                BuyerType = "Individual", BuyerName = "Khách kiểm thử giảm thuế", BuyerAddress = "Địa chỉ kiểm thử",
                SubTotal = 1000000, GrandTotal = 1000000, TotalQuantity = 1 };
            var line = new InvoiceDetail { StoreId = store.StoreId, SourceType = InvoiceDetailSourceType.Manual,
                ItemName = "Hàng hóa được giảm thuế", UnitName = "Hộp", Quantity = 1, UnitPrice = 1000000, Amount = 1000000, TotalAmount = 1000000 };
            head.Details.Add(line); db.Add(head); await db.SaveChangesAsync(); id = head.Id; lineId = line.Id;
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        }
        async Task<string> Protected()
        {
            await using var db = app.Database.CreateHostContext();
            async Task<object> Rows<T>() where T : class
                => (await db.Set<T>().IgnoreQueryFilters().AsNoTracking().ToListAsync()).Select(row => db.Entry(row).Properties
                    .OrderBy(x => x.Metadata.Name).ToDictionary(x => x.Metadata.Name, x => x.CurrentValue)).ToArray();
            return JsonSerializer.Serialize(new { orders = await Rows<Order>(), lines = await Rows<OrderLine>(), payments = await Rows<OrderPayment>(),
                stock = await Rows<InventoryBalance>(), ledger = await Rows<InventoryTransaction>(), menus = await Rows<AdminMenuItem>(), provider = await Rows<InvoiceProviderSetting>() });
        }
        // Existing provider reads upgrade legacy plaintext test credentials once.
        // Finish that normal initialization before taking the unchanged-configuration snapshot.
        using (var initialized = await app.LoginAsync(manager))
        using (var response = await initialized.Http.GetAsync($"/Admin/Invoice/ViettelPayload/{id}"))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var before = await Protected();
        string originalVersion;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
            originalVersion = Convert.ToBase64String((await db.InvoiceHeads.SingleAsync(x => x.Id == id)).RowVersion);
        Dictionary<string,string> Request(string version) => new() { ["InvoiceHeadId"] = id.ToString(), ["Enabled"] = "true", ["Percentage"] = "1", ["ExpectedVersion"] = version };
        using (var limited = await app.LoginAsync(denied))
        using (var response = await limited.Http.PostAsync("/Admin/Invoice/UpdateRevenueTax", new FormUrlEncodedContent(Request(Convert.ToBase64String(new byte[8])))))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var foreign = await app.LoginAsync(other))
        using (var response = await foreign.Http.PostAsync("/Admin/Invoice/UpdateRevenueTax", new FormUrlEncodedContent(Request(originalVersion))))
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var admin = await app.LoginAsync(manager);
        var csrf = admin.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();
        admin.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using (var response = await admin.Http.PostAsync("/Admin/Invoice/UpdateRevenueTax", new FormUrlEncodedContent(Request(originalVersion))))
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        admin.Http.DefaultRequestHeaders.Add("RequestVerificationToken", csrf);
        using (var response = await admin.Http.PostAsync("/Admin/Invoice/UpdateRevenueTax", new FormUrlEncodedContent(Request(Convert.ToBase64String(new byte[8])))))
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
            Assert.Null((await db.InvoiceHeads.SingleAsync(x => x.Id == id)).RevenueTaxPercentage);

        var root = FullApplicationFixture.SourceRoot(); var output = Path.Combine(root, "Logs/invoice-tax-reduction-20261030/browser"); Directory.CreateDirectory(output);
        var start = new ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["NODE_PATH"] = Path.Combine(root, "Logs/pos-offline-browser-deps/node_modules");
        start.ArgumentList.Add(Path.Combine(root, "GaoApp.Tests.Browser/invoice-tax-reduction.browser.cjs"));
        using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { baseUrl = $"http://{store.Host}:{app.Address.Port}", user = manager.Name,
            password = manager.Password, terminalId = store.TerminalId, id, output })); process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(timeout.Token); } catch { if (!process.HasExited) process.Kill(true); throw; }
        var log = await stdout + "\n" + await stderr; await File.WriteAllTextAsync(Path.Combine(output, "browser.log"), log);
        Assert.True(process.ExitCode == 0, log);
        var stale = Request(originalVersion); stale["Percentage"] = "5";
        using (var response = await admin.Http.PostAsync("/Admin/Invoice/UpdateRevenueTax", new FormUrlEncodedContent(stale)))
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using (var response = await admin.Http.PostAsync("/Admin/Invoice/UpdateManualDetail", new FormUrlEncodedContent(new Dictionary<string,string> {
            ["InvoiceDetailId"] = lineId.ToString(), ["Quantity"] = "2", ["UnitPrice"] = "1000000", ["VatRate"] = "0" })))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var head = await db.InvoiceHeads.Include(x => x.Details).SingleAsync(x => x.Id == id);
            Assert.Equal(1, head.RevenueTaxPercentage); Assert.Equal(4000, head.RevenueTaxReductionAmount); Assert.Equal(1996000, head.GrandTotal);
            Assert.Equal(2000000, Assert.Single(head.Details).Amount);
        }
        Assert.Equal(before, await Protected());
    }
}
