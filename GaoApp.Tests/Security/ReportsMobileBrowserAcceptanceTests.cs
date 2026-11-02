using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Reports;
using GaoApp.Domain.Common;
using GaoApp.Domain.Entities;
using GaoApp.Tests.Reports;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class ReportsMobileBrowserAcceptanceTests
{
    private const string Canary = "UI18_PRIVATE_CONFIGURATION_CANARY";

    [Fact]
    public Task Native_report_phone_views_preserve_financial_totals_periods_permissions_and_store_configuration() => RunAsync(false);

    [Fact]
    public Task Diagnostic_native_report_routes_and_financial_scope_without_presentation_matrix() => RunAsync(true);

    [Fact]
    public Task Sales_only_phone_primary_kpi_has_readable_contrast_and_preserves_desktop_and_financials() => RunAsync(false, true);

    private static async Task RunAsync(bool diagnostic, bool contrastOnly = false)
    {
        await using var app = await FullApplicationFixture.StartWithAuditPausedAsync();
        var store = app.Stores[0];
        var manager = await app.AddAccountAsync(store, "*");
        var reader = await app.AddAccountAsync(store, PermissionCodes.Report.Profit.View);
        var salesViewer = await app.AddAccountAsync(store, PermissionCodes.Report.Sales.View);
        var forbidden = await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View);
        var foreign = await app.AddAccountAsync(app.Stores[1], "*");
        using var admin = await app.LoginAsync(manager);
        using var view = await app.LoginAsync(reader);
        using var sales = await app.LoginAsync(salesViewer);
        using var denied = await app.LoginAsync(forbidden);
        using var other = await app.LoginAsync(foreign);

        // Reuse the original owned SQL/POS/treasury fixture; no production data or remote provider.
        var seed = typeof(OperationsReportSqlServerTests).GetMethod("SeedBrowserAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        var seeded = await (Task<object>)seed.Invoke(null, new object[] { app, store, admin })!;
        var dayText = JsonSerializer.SerializeToElement(seeded).GetProperty("Day").GetString()!;
        var day = DateTime.ParseExact(dayText, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        int alternateTerminalId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId)) {
            var terminal = new POSTerminal { StoreId = store.StoreId, Code = "UI18-SECOND", Name = "Quầy kiểm thử thứ hai" };
            db.Add(terminal); await db.SaveChangesAsync(); alternateTerminalId = terminal.Id;
        }
        var alternate = store with { TerminalId = alternateTerminalId };
        using (var second = await app.LoginAsync(await app.AddAccountAsync(alternate, "*"))) {
            await second.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
            var created = await second.JsonAsync(HttpMethod.Post, "/admin/pos/draft");
            var secondId = created.GetProperty("orderId").GetInt32();
            await second.JsonAsync(HttpMethod.Post, $"/admin/pos/{secondId}/items?variantId={store.VariantId}&qty=1");
            await using var db = app.Database.CreateTenantContext(store.StoreId);
            var customerId = await db.Customers.OrderBy(x => x.Id).Select(x => x.Id).FirstAsync();
            await second.JsonAsync(HttpMethod.Post, $"/admin/pos/cart/current/customer/{customerId}", new { repriceExistingLines = false });
            await second.JsonAsync(HttpMethod.Post, $"/admin/pos/{secondId}/finalize-credit", new {
                clientRequestId = Guid.NewGuid(), expectedCustomerId = customerId, expectedBalance = 28000, dueDate = day.AddDays(10) });
        }
        async Task<JsonElement> Expense(string name, decimal amount) => await admin.JsonAsync(HttpMethod.Post,
            "/admin/reports/expenses", new ExpenseWriteDto { ClientRequestId = Guid.NewGuid(), Name = name,
                Category = "other", Amount = amount, RecognitionFrom = day, RecognitionTo = day, IsPaid = false });
        var draft = await Expense("UI18 chi phí nháp", 1000);
        var posted = await Expense("UI18 chi phí đã ghi nhận", 3000);
        posted = await admin.JsonAsync(HttpMethod.Post, $"/admin/reports/expenses/{posted.GetProperty("id").GetInt32()}/confirm",
            new ExpenseActionDto { RowVersion = posted.GetProperty("rowVersion").GetString()! });
        var voided = await Expense("UI18 chi phí đã hủy", 500);
        var voidedId = voided.GetProperty("id").GetInt32();
        voided = await admin.JsonAsync(HttpMethod.Post, $"/admin/reports/expenses/{voidedId}/confirm",
            new ExpenseActionDto { RowVersion = voided.GetProperty("rowVersion").GetString()! });
        await admin.JsonAsync(HttpMethod.Post, $"/admin/reports/expenses/{voidedId}/void",
            new ExpenseActionDto { RowVersion = voided.GetProperty("rowVersion").GetString()!, Reason = "Chuẩn bị fixture riêng UI18" });
        int orderId, cashEntryId, payableId;
        decimal physicalStock, payments;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            db.Add(new StoreBankAccount { StoreId = store.StoreId, BankCode = "LOCAL", BankName = "UI18 fixture",
                AccountName = "UI18 fixture", AccountNumber = "1800000001", IsActive = false,
                ApiSecretEncrypted = Canary, CallbackSecret = Canary });
            await db.SaveChangesAsync();
            orderId = await db.Orders.OrderBy(x => x.Id).Select(x => x.Id).FirstAsync();
            cashEntryId = await db.Set<TreasuryEntry>().Where(x => x.Fund == "cash" && x.ReversalOfId == null).OrderBy(x => x.Id).Select(x => x.Id).FirstAsync();
            payableId = await db.PurchasePayables.OrderBy(x => x.Id).Select(x => x.Id).FirstAsync();
            physicalStock = await db.InventoryBalances.SumAsync(x => x.OnHandQty);
            payments = await db.OrderPayments.SumAsync(x => x.Amount);
        }
        var query = $"?fromDate={dayText}&toDate={dayText}&compare=none&customerState=all";
        var reportPaths = new Dictionary<string, string> {
            ["management"] = "/admin/reports/management/data" + query + "&view=overview",
            ["profit"] = "/admin/reports/profit/data" + query,
            ["cost"] = "/admin/reports/cost-adjustments/data" + query,
            ["cashflow"] = "/admin/reports/operations/cashflow/data" + query,
            ["inventory"] = "/admin/reports/operations/inventory/data" + query,
            ["debts"] = "/admin/reports/operations/debts/data" + query
        };
        async Task<Dictionary<string, JsonElement>> Reports() {
            var result = new Dictionary<string, JsonElement>();
            foreach (var pair in reportPaths) result[pair.Key] = await admin.JsonAsync(HttpMethod.Get, pair.Value);
            return result;
        }
        var baseline = await Reports();
        const decimal scopedTerminalNet = 252000, allTerminalNet = 280000;
        var scoped = await admin.JsonAsync(HttpMethod.Get, reportPaths["management"] + "&terminalId=" + store.TerminalId);
        Assert.Equal(allTerminalNet, baseline["management"].GetProperty("current").GetProperty("summary").GetProperty("netSales").GetProperty("value").GetDecimal());
        Assert.Equal(scopedTerminalNet, scoped.GetProperty("current").GetProperty("summary").GetProperty("netSales").GetProperty("value").GetDecimal());
        var before = await StateAsync(app);
        foreach (var path in new[] { "/admin/reports/profit", "/admin/reports/products", "/admin/reports/sales",
            "/admin/reports/expenses", "/admin/reports/cashflow", "/admin/reports/inventory", "/admin/reports/debts" })
        { using var response = await denied.Http.GetAsync(path); Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); }
        foreach (var path in new[] { "/admin/reports/management/export", "/admin/reports/operations/cashflow/export",
            "/admin/reports/treasury/sources" })
        { using var response = await view.Http.GetAsync(path + query); Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); }
        using (var response = await sales.Http.GetAsync("/admin/reports/profit")) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        foreach (var path in new[] { "/admin/reports/expenses", $"/admin/reports/expenses/{draft.GetProperty("id").GetInt32()}/confirm",
            "/admin/reports/treasury/entries", "/admin/reports/treasury/opening", $"/admin/reports/treasury/payables/{payableId}/due-date" })
        { using var response = await view.Http.PostAsync(path, new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); }
        var foreignExpenses = await other.JsonAsync(HttpMethod.Get, "/admin/reports/expenses/data" + query);
        Assert.DoesNotContain("UI18 chi phí", foreignExpenses.GetRawText());
        Assert.Equal(before, await StateAsync(app));

        var root = FullApplicationFixture.SourceRoot();
        var output = Environment.GetEnvironmentVariable("GAO_UI18_REVIEW_OUTPUT") ?? MobileAcceptanceEvidence.PathFor(root, "TestResults", "mobile-ui-18-20261027", contrastOnly ? "sales-fallback-contrast" : diagnostic ? "diagnostic-browser" : "browser");
        Directory.CreateDirectory(output);
        var start = new ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["NODE_PATH"] = Path.Combine(root, "Logs", "pos-offline-browser-deps", "node_modules");
        start.Environment["GAO_UI18_REVIEW_OUTPUT"] = output;
        start.ArgumentList.Add(Path.Combine(root, "GaoApp.Tests.Browser", "reports-mobile.browser.cjs"));
        if (diagnostic) start.ArgumentList.Add("--diagnostic-native-preflight");
        if (contrastOnly) start.ArgumentList.Add("--sales-fallback-contrast-only");
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
            baseUrl = $"http://{store.Host}:{app.Address.Port}", terminalId = store.TerminalId, output,
            manager = new { user = manager.Name, password = manager.Password },
            reader = new { user = reader.Name, password = reader.Password },
            salesViewer = new { user = salesViewer.Name, password = salesViewer.Password }, day = dayText,
            orderId, expenseDraftId = draft.GetProperty("id").GetInt32(), expensePostedId = posted.GetProperty("id").GetInt32(),
            expenseVoidedId = voidedId, cashEntryId, payableId, canary = Canary, baseline,
            alternateTerminalId, scopedTerminalNet, allTerminalNet }));
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        try { await process.WaitForExitAsync(timeout.Token); } catch { if (!process.HasExited) process.Kill(true); throw; }
        var log = await stdout + "\n" + await stderr;
        await File.WriteAllTextAsync(Path.Combine(output, "chrome.log"), log);
        Assert.True(process.ExitCode == 0, log);
        var result = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "reports-mobile-result.json"))).RootElement;
        Assert.Equal(contrastOnly ? "sales_fallback_contrast_acceptance" : diagnostic ? "diagnostic_native_preflight" : "acceptance", result.GetProperty("mode").GetString());
        Assert.Equal(!diagnostic, result.GetProperty("mandatoryMatrixExecuted").GetBoolean());
        Assert.Equal(contrastOnly ? 1 : 39, result.GetProperty("nativeStateCount").GetInt32());
        var localTimeProofs = result.GetProperty("dateTimeProofs").EnumerateArray().ToList();
        Assert.Equal(contrastOnly ? 0 : 4, localTimeProofs.Count);
        if (!contrastOnly) Assert.True(localTimeProofs.Single(x => x.GetProperty("segment").GetString() == "orders").GetProperty("sourceRows").GetInt32() > 0);
        Assert.All(localTimeProofs, proof => Assert.Equal(0, proof.GetProperty("errors").GetArrayLength()));
        if (!diagnostic) {
            var matrix = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "mobile-review.json"))).RootElement;
            Assert.Equal(contrastOnly ? 1 : 39, matrix.GetProperty("cases").GetArrayLength());
            Assert.Equal(contrastOnly ? 7 : 273, matrix.GetProperty("viewports").GetArrayLength());
            Assert.Equal(contrastOnly ? 2 : 78, matrix.GetProperty("desktopProofs").GetArrayLength());
            Assert.Equal(contrastOnly ? 1 : 39, matrix.GetProperty("printProofs").GetArrayLength());
            if (contrastOnly) {
                var contrastChecks = matrix.GetProperty("primaryKpiContrast").EnumerateArray().ToList();
                Assert.Equal(7, contrastChecks.Count);
                Assert.All(contrastChecks, viewport => {
                    var texts = viewport.GetProperty("checks").EnumerateArray().ToList();
                    Assert.Equal(3, texts.Count);
                    Assert.All(texts, text => Assert.True(text.GetProperty("minimumContrast").GetDouble() >= 4.5));
                });
            }
        }
        Assert.Equal(before, await StateAsync(app));
        Assert.Equal(ReportValues(baseline), ReportValues(await Reports()));
        await using var verify = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(physicalStock, await verify.InventoryBalances.SumAsync(x => x.OnHandQty));
        Assert.Equal(payments, await verify.OrderPayments.SumAsync(x => x.Amount));
        Assert.Equal(4, await verify.Orders.CountAsync());
        Assert.Equal(3000m, await verify.Set<OperatingExpense>().Where(x => x.Status == "confirmed").SumAsync(x => x.Amount));
        await File.WriteAllTextAsync(Path.Combine(output, "sql-checks.json"), JsonSerializer.Serialize(new {
            passed = true, fixtureOnly = true, diagnosticOnly = diagnostic, focusedSalesContrastOnly = contrastOnly, mandatoryPresentationMatrixVerified = !diagnostic,
            physicalStock, payments, orders = 4, recognizedExpenses = 3000,
            scopedTerminalNet, allTerminalNet, twoTerminalSourceScopesReconciled = true,
            originalReportPayloadsUnchanged = true, allFinancialScalarRowsAndRowVersionsUnchanged = true,
            menusRightsAndStoreConfigurationUnchanged = true, forbiddenReadAndCommandsStatus = 403,
            foreignExpenseExcluded = true, noBrowserFinancialWrite = true, noExternalProvider = true
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string ReportValues(Dictionary<string, JsonElement> reports)
    {
        var values = new JsonObject();
        foreach (var pair in reports) {
            var payload = JsonNode.Parse(pair.Value.GetRawText())!.AsObject();
            // Only response-envelope creation metadata is volatile. All monetary values,
            // periods, quality flags, nulls, rows and source identifiers remain compared.
            payload.Remove("generatedAtUtc"); payload.Remove("generation");
            values[pair.Key] = payload;
        }
        return values.ToJsonString();
    }

    private static async Task<string> StateAsync(FullApplicationFixture app)
    {
        await using var db = app.Database.CreateHostContext();
        async Task<object> Rows<TEntity>() where TEntity : BaseEntity {
            var rows = await db.Set<TEntity>().IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).ToListAsync();
            return rows.Select(row => db.Entry(row).Properties.OrderBy(x => x.Metadata.Name)
                .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue)).ToList();
        }
        return JsonSerializer.Serialize(new {
            stores = await Rows<Store>(), menus = await Rows<AdminMenuItem>(), roles = await Rows<Role>(),
            grants = await db.RolePermissions.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.RoleId, x.PermissionId }).ToListAsync(),
            userStoreRoles = await db.UserInStores.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.UserId, x.StoreId, x.RoleId }).ToListAsync(),
            banks = await Rows<StoreBankAccount>(), products = await Rows<Product>(), variants = await Rows<ProductVariant>(),
            customers = await Rows<Customer>(), terminals = await Rows<POSTerminal>(), shifts = await Rows<POSShift>(),
            orders = await Rows<Order>(), lines = await Rows<OrderLine>(), payments = await Rows<OrderPayment>(),
            stock = await Rows<InventoryBalance>(), movements = await Rows<InventoryTransaction>(),
            valuation = await Rows<InventoryValuationEntry>(), layers = await Rows<InventoryCostLayer>(),
            allocations = await Rows<InventoryCostLayerAllocation>(), costBudgets = await Rows<InventoryCostBudgetDebit>(),
            expenses = await Rows<OperatingExpense>(), treasury = await Rows<TreasuryEntry>(), openings = await Rows<TreasuryOpeningBalance>(),
            payables = await Rows<PurchasePayable>(), receivables = await Rows<CustomerReceivableEntry>(),
            stockDocuments = await Rows<StockDocument>()
        });
    }
}
