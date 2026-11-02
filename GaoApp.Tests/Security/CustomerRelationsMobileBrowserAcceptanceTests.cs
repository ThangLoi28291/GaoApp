using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Common;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class CustomerRelationsMobileBrowserAcceptanceTests
{
    [Fact]
    public async Task Native_customer_supplier_debt_deposit_and_rewards_phone_views_preserve_balances_permissions_and_configuration()
    {
        await using var app = await FullApplicationFixture.StartWithAuditPausedAsync();
        var store = app.Stores[0];
        var manager = await app.AddAccountAsync(store,"*");
        var viewer = await app.AddAccountAsync(store,PermissionCodes.Catalog.Customer.View,PermissionCodes.Catalog.Supplier.View,
            PermissionCodes.CustomerDebt.View,PermissionCodes.CustomerDeposit.View);
        var blockedAccount = await app.AddAccountAsync(store,PermissionCodes.Pos.Order.View);
        var otherAccount = await app.AddAccountAsync(app.Stores[1],"*");
        var profile = await CustomerProfileSqlServerTests.SeedAsync(app,store,manager.UserId);
        int supplierId,debtOrderId,depositOrderId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var customer = await db.Customers.SingleAsync(x => x.Id == profile.CustomerId);
            customer.HaveDebt = true;
            var supplier = await db.Suppliers.SingleAsync(); supplier.Name = "Nhà cung cấp UI-15";
            supplier.Code = "UI15-SUPPLIER"; supplier.Note = "UI15 supplier original note";
            supplier.BankAccountNumber = "150015001500"; supplier.BankAccountName = "CUA HANG THU UI15"; supplier.BankName = "NGAN HANG THU UI15";
            var shift = await db.POSShifts.SingleAsync(); shift.ShiftCode = "UI15-OPEN";
            var credit = new Order { StoreId = store.StoreId,CustomerId = customer.Id,POSShiftId = shift.Id,
                OrderNumber = "UI15-DEBT",Status = OrderStatus.Completed,IsCreditSale = true,Subtotal = 60000,GrandTotal = 60000,
                BalanceDue = 60000,PaymentStatus = PaymentStatus.Unpaid,CompletedAtUtc = DateTime.UtcNow,
                CreditDueDate = DateTime.UtcNow.AddDays(-1).Date };
            var depositOrder = new Order { StoreId = store.StoreId,CustomerId = customer.Id,POSShiftId = shift.Id,
                OrderNumber = "UI15-DEPOSIT-ORDER",Status = OrderStatus.Completed,Subtotal = 50000,GrandTotal = 50000,PaidTotal = 50000,
                DepositAmount = 50000,PaymentStatus = PaymentStatus.Paid,CompletedAtUtc = DateTime.UtcNow };
            db.AddRange(credit,depositOrder);
            db.Add(new CustomerRewardVoucher { StoreId = store.StoreId,CustomerId = customer.Id,VoucherCode = "VC-UI15-EXPIRED",
                Value = 20000,RequiredAmount = 2000,IssuedAtUtc = DateTime.UtcNow.AddDays(-60),Status = CustomerRewardVoucherStatus.Expired });
            await db.SaveChangesAsync();
            db.Add(new CustomerReceivableEntry { StoreId = store.StoreId,CustomerId = customer.Id,OrderId = credit.Id,
                Amount = 60000,Kind = "Sale",Note = "UI15 read fixture credit source" });
            await db.SaveChangesAsync();
            supplierId = supplier.Id; debtOrderId = credit.Id; depositOrderId = depositOrder.Id;
        }
        using var admin = await app.LoginAsync(manager);
        using var reader = await app.LoginAsync(viewer);
        using var denied = await app.LoginAsync(blockedAccount);
        using var foreign = await app.LoginAsync(otherAccount);
        var collect = new { clientRequestId = Guid.NewGuid(),customerId = profile.CustomerId,orderId = debtOrderId,amount = 10000,method = 0,note = "UI15 source collection" };
        var collection = await admin.JsonAsync(HttpMethod.Post,"/admin/customer-debt/collect",collect);
        var replayCollection = await admin.JsonAsync(HttpMethod.Post,"/admin/customer-debt/collect",collect);
        Assert.Equal(collection.GetProperty("receiptId").GetInt32(),replayCollection.GetProperty("receiptId").GetInt32());
        var receive = new { clientRequestId = Guid.NewGuid(),customerId = profile.CustomerId,amount = 250000,method = 0,purpose = "UI15 giao hàng theo đơn",note = "UI15 source deposit" };
        var deposit = await admin.JsonAsync(HttpMethod.Post,"/admin/customer-deposit/receive",receive);
        var replayDeposit = await admin.JsonAsync(HttpMethod.Post,"/admin/customer-deposit/receive",receive);
        var depositId = deposit.GetProperty("depositId").GetInt32();
        Assert.Equal(depositId,replayDeposit.GetProperty("depositId").GetInt32());
        var refund = new { clientRequestId = Guid.NewGuid(),depositId,amount = 40000,method = 0,note = "UI15 source refund" };
        var refunded = await admin.JsonAsync(HttpMethod.Post,"/admin/customer-deposit/refund",refund);
        var replayRefund = await admin.JsonAsync(HttpMethod.Post,"/admin/customer-deposit/refund",refund);
        Assert.Equal(refunded.GetProperty("entryId").GetInt32(),replayRefund.GetProperty("entryId").GetInt32());
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            (await db.Set<CustomerDeposit>().SingleAsync(x => x.Id == depositId)).Balance -= 50000;
            (await db.Orders.SingleAsync(x => x.Id == depositOrderId)).CustomerDepositId = depositId;
            db.Add(new CustomerDepositEntry { StoreId = store.StoreId,CustomerDepositId = depositId,OrderId = depositOrderId,
                POSShiftId = await db.POSShifts.Select(x => x.Id).SingleAsync(),Kind = "Apply",Amount = -50000,
                Note = "UI15 read fixture linked order source" });
            await db.SaveChangesAsync();
        }
        var before = await ProtectedStateAsync(app,profile.CustomerId,supplierId);
        foreach (var path in new[] { "/Admin/Customer", "/Admin/Supplier", "/admin/customer-debt", "/admin/customer-debt/collections",
            "/admin/customer-deposit", "/admin/reward-vouchers", "/admin/reward-vouchers/settings" })
        { using var response = await denied.Http.GetAsync(path); Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode); }
        foreach (var controller in new[] { "Customer","Supplier" })
        {
            using var create = await reader.Http.GetAsync($"/Admin/{controller}/Create"); Assert.Equal(HttpStatusCode.Forbidden,create.StatusCode);
            using var edit = await reader.Http.GetAsync($"/Admin/{controller}/Edit/{(controller == "Customer" ? profile.CustomerId : supplierId)}"); Assert.Equal(HttpStatusCode.Forbidden,edit.StatusCode);
        }
        foreach (var path in new[] { "/admin/customer-debt/collect", "/admin/customer-deposit/receive", "/admin/customer-deposit/refund" })
        { using var response = await reader.Http.PostAsJsonAsync(path,new { }); Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode); }
        using (var settings = await reader.Http.GetAsync("/admin/reward-vouchers/settings")) Assert.Equal(HttpStatusCode.Forbidden,settings.StatusCode);
        var profilePath = $"/admin/customers/{profile.CustomerId}/profile";
        using (var orders = await reader.Http.GetAsync(profilePath+"/data?tab=orders")) Assert.Equal(HttpStatusCode.Forbidden,orders.StatusCode);
        var privateOverview = await reader.JsonAsync(HttpMethod.Get,profilePath+"/data");
        Assert.DoesNotContain(privateOverview.GetProperty("items").EnumerateArray(),x => x.GetProperty("kind").GetString() == "order" || x.GetProperty("orderId").ValueKind != JsonValueKind.Null);
        foreach (var path in new[] { profilePath,profilePath+"/data?tab=points",$"/admin/reward-vouchers/{profile.VoucherId}/quick-view",$"/admin/customer-deposit/{depositId}/print" })
        { using var response = await foreign.Http.GetAsync(path); Assert.Equal(HttpStatusCode.NotFound,response.StatusCode); }
        var csrf = admin.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();
        admin.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using (var response = await admin.Http.PostAsJsonAsync("/admin/customer-debt/collect",collect)) Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        admin.Http.DefaultRequestHeaders.Add("RequestVerificationToken",csrf);
        Assert.Equal(before,await ProtectedStateAsync(app,profile.CustomerId,supplierId));
        var root = FullApplicationFixture.SourceRoot();
        var output = Environment.GetEnvironmentVariable("GAO_UI15_REVIEW_OUTPUT") ?? MobileAcceptanceEvidence.PathFor(root,"TestResults","mobile-ui-15-20261026","browser");
        Directory.CreateDirectory(output);
        var start = new ProcessStartInfo("node") { WorkingDirectory = root,UseShellExecute = false,CreateNoWindow = true,
            RedirectStandardInput = true,RedirectStandardOutput = true,RedirectStandardError = true };
        start.Environment["NODE_PATH"] = Path.Combine(root,"Logs","pos-offline-browser-deps","node_modules");
        start.ArgumentList.Add(Path.Combine(root,"GaoApp.Tests.Browser","customer-relations-mobile.browser.cjs"));
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { baseUrl = $"http://{store.Host}:{app.Address.Port}",
            terminalId = store.TerminalId,output,manager = new { user = manager.Name,password = manager.Password },
            viewer = new { user = viewer.Name,password = viewer.Password },customerId = profile.CustomerId,orderId = profile.OrderId,
            voucherId = profile.VoucherId,supplierId,debtOrderId,depositId,depositOrderId,
            customerNote = "UI15 native customer note",supplierNote = "UI15 native supplier note" }));
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(18));
        try { await process.WaitForExitAsync(timeout.Token); } catch { if (!process.HasExited) process.Kill(true); throw; }
        var log = await stdout+"\n"+await stderr; await File.WriteAllTextAsync(Path.Combine(output,"chrome.log"),log);
        Assert.True(process.ExitCode == 0,log);
        Assert.Equal(before,await ProtectedStateAsync(app,profile.CustomerId,supplierId));
        await using var verify = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal("UI15 native customer note",await verify.Customers.Where(x => x.Id == profile.CustomerId).Select(x => x.Note).SingleAsync());
        Assert.Equal("UI15 native supplier note",await verify.Suppliers.Where(x => x.Id == supplierId).Select(x => x.Note).SingleAsync());
        Assert.Equal(50000m,await verify.Orders.Where(x => x.Id == debtOrderId).Select(x => x.BalanceDue).SingleAsync());
        Assert.Equal(50000m,await verify.Set<CustomerReceivableEntry>().SumAsync(x => x.Amount));
        Assert.Equal(160000m,await verify.Set<CustomerDeposit>().Where(x => x.Id == depositId).Select(x => x.Balance).SingleAsync());
        Assert.Equal(160000m,await verify.Set<CustomerDepositEntry>().SumAsync(x => x.Amount));
        Assert.Equal(11500m,await verify.CustomerRewardLedgers.SumAsync(x => x.Amount));
        Assert.Equal(23,await verify.CustomerRewardLedgers.CountAsync());
        Assert.Equal(3,await verify.CustomerRewardVouchers.CountAsync());
        Assert.Equal(1,await verify.Set<CustomerDebtReceipt>().CountAsync());
        Assert.Equal(3,await verify.Set<CustomerDepositEntry>().CountAsync());
        Assert.Equal(100m,await verify.InventoryBalances.Select(x => x.OnHandQty).SingleAsync());
        await File.WriteAllTextAsync(Path.Combine(output,"sql-checks.json"),JsonSerializer.Serialize(new {
            passed = true,receivable = 50000m,deposit = 160000m,rewardLedgerAmount = 11500m,rewardEntries = 23,vouchers = 3,
            seedNativeCollectionReceiveAndRefundReplayStable = true,onlyTwoNativeNotesChanged = true,
            storesMenusSettingsPermissionsMoneyStockAndOtherTenantPreserved = true,profileOrderPrivacyPreserved = true,
            nativeCreateUpdateCollectReceiveRefundAndSettingsRightsPreserved = true,fixtureOnly = true
        },new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task<string> ProtectedStateAsync(FullApplicationFixture app,int customerId,int supplierId)
    {
        await using var db = app.Database.CreateHostContext();
        async Task<object> Rows<TEntity>(params int[] noteOnlyIds) where TEntity : BaseEntity
        {
            var rows = await db.Set<TEntity>().IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).ToListAsync();
            var audit = new[] { "Note","RowVersion","UpdatedAtUtc","UpdatedBy" };
            return rows.Select(row => db.Entry(row).Properties.OrderBy(x => x.Metadata.Name)
                .Where(p => !noteOnlyIds.Contains(row.Id) || !audit.Contains(p.Metadata.Name))
                .ToDictionary(p => p.Metadata.Name,p => p.CurrentValue)).ToList();
        }
        return JsonSerializer.Serialize(new { stores = await Rows<Store>(),menus = await Rows<AdminMenuItem>(),roles = await Rows<Role>(),
            grants = await db.RolePermissions.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id,x.RoleId,x.PermissionId }).ToListAsync(),
            customers = await Rows<Customer>(customerId),suppliers = await Rows<Supplier>(supplierId),products = await Rows<Product>(),variants = await Rows<ProductVariant>(),
            settings = await Rows<RewardSettings>(),invoice = await Rows<InvoiceProviderSetting>(),automatic = await Rows<AutoInvoiceSettings>(),
            orders = await Rows<Order>(),lines = await Rows<OrderLine>(),payments = await Rows<OrderPayment>(),returns = await Rows<SalesReturn>(),
            receivables = await Rows<CustomerReceivableEntry>(),receipts = await Rows<CustomerDebtReceipt>(),deposits = await Rows<CustomerDeposit>(),
            depositEntries = await Rows<CustomerDepositEntry>(),rewardLedger = await Rows<CustomerRewardLedger>(),vouchers = await Rows<CustomerRewardVoucher>(),
            shifts = await Rows<POSShift>(),cash = await Rows<POSShiftCashTransaction>(),banks = await Rows<StoreBankAccount>(),
            stock = await Rows<InventoryBalance>(),ledger = await Rows<InventoryTransaction>() });
    }
}
