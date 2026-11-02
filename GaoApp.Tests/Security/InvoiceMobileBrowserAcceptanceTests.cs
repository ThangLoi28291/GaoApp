using System.Diagnostics;
using System.Net;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Common;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class InvoiceMobileBrowserAcceptanceTests
{
    private const string Canary = "UI16_PRIVATE_CREDENTIAL_CANARY";

    [Fact]
    public async Task Native_invoice_phone_views_preserve_totals_stock_locks_privacy_permissions_and_provider_configuration()
    {
        await using var app = await FullApplicationFixture.StartWithAuditPausedAsync();
        var store = app.Stores[0];
        var manager = await app.AddAccountAsync(store,"*");
        var ordinary = await app.AddAccountAsync(store,"*");
        var reader = await app.AddAccountAsync(store,PermissionCodes.System.Integration.View,PermissionCodes.Inventory.Transaction.View);
        var forbidden = await app.AddAccountAsync(store,PermissionCodes.Pos.Order.View);
        var foreign = await app.AddAccountAsync(app.Stores[1],"*");
        await using (var roles = app.Database.CreateHostContext())
        {
            var adminRole = await roles.Roles.SingleAsync(x => x.StoreId == store.StoreId && x.Code == "ADMIN" && x.IsSystemRole);
            (await roles.UserInStores.SingleAsync(x => x.StoreId == store.StoreId && x.UserId == manager.UserId)).RoleId = adminRole.Id;

            await roles.SaveChangesAsync();
        }
        var orderId = await InvoiceOrderDraftSqlServerTests.SeedOrder(app,manager,"UI16-MANUAL-ORDER",InvoiceIssuanceRoute.Manual);
        var issuedOrderId = await InvoiceOrderDraftSqlServerTests.SeedOrder(app,manager,"UI16-ISSUED-ORDER",InvoiceIssuanceRoute.Manual);
        var draftOrderId = await InvoiceOrderDraftSqlServerTests.SeedOrder(app,manager,"UI16-EMPTY-DRAFT-CANDIDATE",InvoiceIssuanceRoute.Manual);
        using var admin = await app.LoginAsync(manager);
        using var staff = await app.LoginAsync(ordinary);
        using var view = await app.LoginAsync(reader);
        using var denied = await app.LoginAsync(forbidden);
        using var other = await app.LoginAsync(foreign);
        using var created = await admin.Http.PostAsync("/Admin/Invoice/CreateOrderDraft",new FormUrlEncodedContent(new Dictionary<string,string> { ["orderId"] = orderId.ToString() }));
        Assert.Equal(HttpStatusCode.OK,created.StatusCode);
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var invoiceId = createdJson.RootElement.GetProperty("invoices")[0].GetProperty("id").GetInt32();
        int issuedInvoiceId,manualLineId,providerId,logId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var provider = await db.InvoiceProviderSettings.SingleAsync(); provider.Username = "UI16-SYNTHETIC-ACCOUNT";
            provider.Password = Canary; providerId = provider.Id;
            var warehouse = await db.Warehouses.SingleAsync(x => x.Id == store.WarehouseId);
            var legal = await db.LegalEntities.SingleAsync(x => x.Id == warehouse.LegalEntityId);
            legal.DefaultWarehouseId = warehouse.Id;
            var head = await db.InvoiceHeads.SingleAsync(x => x.Id == invoiceId);
            head.LegalEntityId = legal.Id;
            head.BuyerType = "Individual"; head.BuyerName = "Người mua kiểm thử UI-16"; head.BuyerAddress = "Địa chỉ thử UI-16";
            head.TotalQuantity = 1.25m; head.SubTotal = 10000; head.VatAmount = 800; head.GrandTotal = 10800;
            var line = new InvoiceDetail { StoreId = store.StoreId,InvoiceHeadId = head.Id,SourceType = InvoiceDetailSourceType.Manual,
                ProductVariantId = store.VariantId,ItemName = "Dòng thủ công UI-16",UnitName = "Hộp",Quantity = 1.25m,UnitPrice = 8000,
                Amount = 10000,VatRate = 8,VatAmount = 800,TotalAmount = 10800,Note = "UI16 original line note" };
            var issued = new InvoiceHead { StoreId = store.StoreId,OrderId = issuedOrderId,InvoiceProviderSettingId = providerId,LegalEntityId = legal.Id,
                InvoiceNumber = "UI16-ISSUED",InvoiceDate = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(7),DateTimeKind.Unspecified),
                ProviderCode = "VIETTEL",ProviderStatus = InvoiceProviderStatus.Issued,ProviderInvoiceNo = "UI16-000001",
                TransactionUuid = Guid.NewGuid().ToString(),IsLocked = true,IssuedAtUtc = DateTime.UtcNow,CodeOfTax = "UI16-SYNTHETIC-TAX-ACCEPTED",
                BuyerType = "Individual",BuyerName = "Người mua đã phát hành UI-16",BuyerAddress = "Địa chỉ thử",
                TotalQuantity = 2,SubTotal = 10000,VatAmount = 800,GrandTotal = 10800 };
            issued.Details.Add(new InvoiceDetail { StoreId = store.StoreId,ProductVariantId = store.VariantId,
                SourceType = InvoiceDetailSourceType.FromOrderLine,OrderLineId = await db.OrderLines.Where(x => x.OrderId == issuedOrderId).Select(x => x.Id).SingleAsync(),
                ItemName = "Dòng hóa đơn đã khóa UI-16",UnitName = "Hộp",Quantity = 2,UnitPrice = 5000,Amount = 10000,VatRate = 8,VatAmount = 800,TotalAmount = 10800 });
            db.AddRange(line,issued);
            db.Add(new InvoiceInputStockSupplementalMovement { StoreId = store.StoreId,WarehouseId = warehouse.Id,
                ProductVariantId = store.VariantId,QuantityChange = 20,EffectiveAtUtc = DateTime.UtcNow,
                MovementType = InvoiceInputStockSupplementalMovementType.LegacyOpening,LegacySourceKey = "UI16-DOCUMENTARY-OPENING",
                Note = "Số đầu kỳ tồn chứng từ trong fixture UI-16" });
            db.Add(new AutoInvoiceSettings { StoreId = store.StoreId,IsEnabled = false });
            await db.SaveChangesAsync(); manualLineId = line.Id; issuedInvoiceId = issued.Id;
            var log = new InvoiceIntegrationLog { StoreId = store.StoreId,InvoiceHeadId = invoiceId,
                ActionType = InvoiceIntegrationActionType.IssueInvoice,IsSuccess = false,ErrorCode = "HTTP_500",
                ErrorMessage = "HTTP 500 timeout password="+Canary,RequestUrl = "https://provider.invalid/api?api_key="+Canary,
                RequestBody = JsonSerializer.Serialize(new { password = Canary,invoice = "UI16-MANUAL-ORDER" }),
                ResponseBody = JsonSerializer.Serialize(new { statusCode = 500,requestId = "UI16-PUBLIC-TRACE",password = Canary }),
                StartedAtUtc = DateTime.UtcNow.AddMinutes(-2),FinishedAtUtc = DateTime.UtcNow.AddMinutes(-1),DurationMs = 1000 };
            db.Add(log); await db.SaveChangesAsync(); logId = log.Id;
        }
        // Complete the native legacy credential protection in this owned fixture before the read-only boundary.
        using (var prepared = await admin.Http.GetAsync($"/Admin/Invoice/ViettelPayload/{invoiceId}"))
        {
            Assert.Equal(HttpStatusCode.OK,prepared.StatusCode);
            Assert.DoesNotContain(Canary,WebUtility.HtmlDecode(await prepared.Content.ReadAsStringAsync()));
        }
        await using (var prepared = app.Database.CreateTenantContext(store.StoreId))
        {
            var stored = await prepared.InvoiceProviderSettings.Where(x => x.Id == providerId).Select(x => x.Password).SingleAsync();
            Assert.StartsWith("gdp:v1:",stored); Assert.DoesNotContain(Canary,stored);
        }
        var before = await StateAsync(app);
        foreach (var path in new[] { "/Admin/Invoice", "/Admin/InvoiceDashboard", "/Admin/InvoiceProviderSetting", "/Admin/InvoiceIntegrationLog", "/admin/invoice-input-stock", "/Admin/AutoInvoice", "/Admin/ViettelInvoiceSync" })
        { using var response = await denied.Http.GetAsync(path); Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode); }
        foreach (var path in new[] { "/Admin/Invoice",$"/Admin/Invoice/Detail/{invoiceId}","/Admin/InvoiceProviderSetting/Edit/"+providerId,"/Admin/InvoiceIntegrationLog/Cleanup","/Admin/AutoInvoice" })
        { using var response = await view.Http.GetAsync(path); Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode); }
        using (var response = await staff.Http.GetAsync($"/Admin/InvoiceCorrection/Create?originalInvoiceHeadId={issuedInvoiceId}&type=1")) Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        foreach (var path in new[] { "/Admin/InvoiceDashboard", "/Admin/InvoiceProviderSetting",$"/Admin/InvoiceIntegrationLog/Detail/{logId}" })
        {
            using var response = await view.Http.GetAsync(path); Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            Assert.DoesNotContain(Canary,WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        }
        using (var response = await other.Http.GetAsync($"/Admin/Invoice/Detail/{invoiceId}"))
        { Assert.False(response.IsSuccessStatusCode); }
        using (var response = await other.Http.GetAsync($"/Admin/InvoiceIntegrationLog/Detail/{logId}")) Assert.Equal(HttpStatusCode.Found,response.StatusCode);
        Assert.Equal(before,await StateAsync(app));
        var root = FullApplicationFixture.SourceRoot();
        var output = Environment.GetEnvironmentVariable("GAO_UI16_REVIEW_OUTPUT") ?? MobileAcceptanceEvidence.PathFor(root,"TestResults","mobile-ui-16-20261027","browser");
        Directory.CreateDirectory(output);
        var start = new ProcessStartInfo("node") { WorkingDirectory = root,UseShellExecute = false,CreateNoWindow = true,RedirectStandardInput = true,RedirectStandardOutput = true,RedirectStandardError = true };
        start.Environment["NODE_PATH"] = Path.Combine(root,"Logs","pos-offline-browser-deps","node_modules");
        start.Environment["GAO_UI16_REVIEW_OUTPUT"] = output;
        start.ArgumentList.Add(Path.Combine(root,"GaoApp.Tests.Browser","invoice-mobile.browser.cjs"));
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { baseUrl = $"http://{store.Host}:{app.Address.Port}",terminalId = store.TerminalId,output,
            manager = new { user = manager.Name,password = manager.Password },viewer = new { user = reader.Name,password = reader.Password },
            invoiceId,issuedInvoiceId,orderId,draftOrderId,manualLineId,providerId,logId,canary = Canary }));
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        try { await process.WaitForExitAsync(timeout.Token); } catch { if (!process.HasExited) process.Kill(true); throw; }
        var logOutput = await stdout+"\n"+await stderr; await File.WriteAllTextAsync(Path.Combine(output,"chrome.log"),logOutput);
        Assert.True(process.ExitCode == 0,logOutput);
        Assert.Equal(before,await StateAsync(app));
        await using var verify = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(10800m,await verify.InvoiceHeads.Where(x => x.Id == invoiceId).Select(x => x.GrandTotal).SingleAsync());
        Assert.Equal(1.25m,await verify.InvoiceDetails.Where(x => x.Id == manualLineId).Select(x => x.Quantity).SingleAsync());
        Assert.Equal(100m,await verify.InventoryBalances.Select(x => x.OnHandQty).SingleAsync());
        var documentary = await new GaoApp.Infrastructure.Repositories.Invoices.InvoiceInputStockRepository(verify).GetAvailabilityAsync(invoiceId);
        Assert.Equal(18m,Assert.Single(documentary.Lines).AvailableBaseQuantity);
        Assert.Empty(await verify.AutoInvoiceOperations.ToListAsync());
        await File.WriteAllTextAsync(Path.Combine(output,"sql-checks.json"),JsonSerializer.Serialize(new { passed = true,
            invoiceQuantity = 1.25m,invoiceSubtotal = 10000m,vat = 800m,total = 10800m,physicalStock = 100m,documentaryStock = 18m,
            allInvoicesLocksOrdersStockProviderSettingsMenusRolesCredentialsAndRawLogsUnchanged = true,
            providerAndIssuanceCommands = 0,settingsEnabled = false,providerCredentialProtectedBeforeReadOnlyBoundary = true,
            logCanaryRemainsOnlyInOriginalStorage = true,fixtureOnly = true },new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task<string> StateAsync(FullApplicationFixture app)
    {
        await using var db = app.Database.CreateHostContext();
        async Task<object> Rows<TEntity>() where TEntity : BaseEntity
        {
            var rows = await db.Set<TEntity>().IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).ToListAsync();
            return rows.Select(row => db.Entry(row).Properties.OrderBy(x => x.Metadata.Name).ToDictionary(p => p.Metadata.Name,p => p.CurrentValue)).ToList();
        }
        return JsonSerializer.Serialize(new { stores = await Rows<Store>(),menus = await Rows<AdminMenuItem>(),roles = await Rows<Role>(),
            grants = await db.RolePermissions.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id,x.RoleId,x.PermissionId }).ToListAsync(),
            userStoreRoles = await db.UserInStores.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id,x.UserId,x.StoreId,x.RoleId }).ToListAsync(),
            providers = await Rows<InvoiceProviderSetting>(),automatic = await Rows<AutoInvoiceSettings>(),operations = await Rows<AutoInvoiceOperation>(),
            heads = await Rows<InvoiceHead>(),details = await Rows<InvoiceDetail>(),logs = await Rows<InvoiceIntegrationLog>(),
            orders = await Rows<Order>(),lines = await Rows<OrderLine>(),payments = await Rows<OrderPayment>(),
            stock = await Rows<InventoryBalance>(),ledger = await Rows<InventoryTransaction>(),documentary = await Rows<InvoiceInputStockSupplementalMovement>(),
            products = await Rows<Product>(),variants = await Rows<ProductVariant>(),customers = await Rows<Customer>(),
            warehouses = await Rows<Warehouse>(),legalEntities = await Rows<LegalEntity>(),rewardSettings = await Rows<RewardSettings>() });
    }
}
