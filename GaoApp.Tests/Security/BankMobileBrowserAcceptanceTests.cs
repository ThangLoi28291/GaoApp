using System.Diagnostics;
using System.Net;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Common;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Web.Services.Acb;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class BankMobileBrowserAcceptanceTests
{
    private const string Canary = "UI17_PRIVATE_CREDENTIAL_CANARY";

    [Fact]
    public async Task Native_bank_phone_views_preserve_money_evidence_live_selection_permissions_and_store_configuration()
    {
        await using var app = await FullApplicationFixture.StartWithAuditPausedAsync();
        var store = app.Stores[0];
        var manager = await app.AddAccountAsync(store,"*");
        var ordinary = await app.AddAccountAsync(store,"*");
        var reader = await app.AddAccountAsync(store,PermissionCodes.System.BankAccount.View,PermissionCodes.Pos.Payment.View);
        var forbidden = await app.AddAccountAsync(store,PermissionCodes.Pos.Order.View);
        var foreign = await app.AddAccountAsync(app.Stores[1],"*");
        await using (var host = app.Database.CreateHostContext())
        {
            (await host.Users.SingleAsync(x => x.Id == manager.UserId)).IsHostAdmin = true;
            await host.SaveChangesAsync();
        }
        using var admin = await app.LoginAsync(manager);
        using var staff = await app.LoginAsync(ordinary);
        using var view = await app.LoginAsync(reader);
        using var denied = await app.LoginAsync(forbidden);
        using var other = await app.LoginAsync(foreign);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            (await db.ProductVariants.SingleAsync(x => x.Id == store.VariantId)).Price = 10000;
            await db.SaveChangesAsync();
        }
        await admin.JsonAsync(HttpMethod.Post,"/admin/pos/shift/open",new { openingCash = 0,warehouseId = store.WarehouseId });
        async Task<int> OrderAsync()
        {
            var created = await admin.JsonAsync(HttpMethod.Post,"/admin/pos/cart/current/new",new { });
            var id = created.GetProperty("orderId").GetInt32();
            await admin.JsonAsync(HttpMethod.Post,$"/admin/pos/{id}/items?variantId={store.VariantId}&qty=1",new { });
            await admin.JsonAsync(HttpMethod.Post,"/admin/pos/cart/current/hold",new { holdNote = "UI-17 owned fixture preparation" });
            return id;
        }
        var orderId = await OrderAsync(); var reviewOrderId = await OrderAsync();
        var waitingOrderId = await OrderAsync(); var secondWaitingOrderId = await OrderAsync();
        int bankId,receiptId,qrId,secondQrId;
        var businessDate = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(7).Date,DateTimeKind.Unspecified);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var shift = await db.POSShifts.SingleAsync(); shift.CurrentOrderId = null;
            var bank = new StoreBankAccount { StoreId = store.StoreId,BankCode = "ACB",BankName = "ACB UI-17 fixture",
                AccountNumber = "1700000001",AccountName = "CUA HANG KIEM THU UI17",IsDefault = true,IsActive = true,
                ConfirmMode = BankQrConfirmMode.Manual,QrRenderMode = BankQrRenderMode.LocalEmvQr,VietQrBankBin = "970416",
                ApiSecretEncrypted = Canary,CallbackSecret = Canary };
            db.Add(bank); await db.SaveChangesAsync(); bankId = bank.Id;
            var protocol = new AcbProtocol(new HttpClient(),new EphemeralDataProtectionProvider());
            db.Add(new StoreAcbSettings { StoreId = store.StoreId,BankAccountId = bankId,Enabled = false,
                ClientId = "ui17-synthetic-client",ClientSecretProtected = protocol.Protect(store.StoreId,Canary),
                CallbackApiKeyProtected = protocol.Protect(store.StoreId,Canary),MerchantId = "UI17-OWN-MERCHANT" });
            var orders = await db.Orders.OrderBy(x => x.Id).ToListAsync();
            foreach (var order in orders) { order.OrderNumber = "UI17-ORDER-"+order.Id; order.Status = OrderStatus.Draft; }
            var matchedOrder = orders.Single(x => x.Id == orderId);
            Assert.Equal(10000m,matchedOrder.GrandTotal);
            matchedOrder.Status = OrderStatus.Completed; matchedOrder.PaidTotal = 10000;
            matchedOrder.BalanceDue = 0; matchedOrder.PaymentStatus = PaymentStatus.Paid;
            var payment = new OrderPayment { StoreId = store.StoreId,OrderId = orderId,Method = PaymentMethod.BankTransfer,
                Amount = 10000,ReferenceCode = "UI17-ACB-MATCHED",Provider = "ACB",PaidAtUtc = DateTime.UtcNow };
            db.Add(payment); await db.SaveChangesAsync();
            PosPaymentQrRequest Qr(int id,string code,bool automatic) => new() { StoreId = store.StoreId,OrderId = id,
                BankAccountId = bankId,Amount = 10000,RequestCode = code,Content = "UI17 synthetic evidence",
                ConfirmMode = automatic ? BankQrConfirmMode.Polling : BankQrConfirmMode.Manual,
                QrRenderMode = automatic ? BankQrRenderMode.ProviderApi : BankQrRenderMode.LocalEmvQr,
                ExpireAtUtc = DateTime.UtcNow.AddHours(4),Status = PosPaymentQrStatus.Pending };
            var matchedQr = Qr(orderId,"UI17-QR-MATCHED",true); matchedQr.Status = PosPaymentQrStatus.Paid; matchedQr.PaymentId = payment.Id;
            var reviewQr = Qr(reviewOrderId,"UI17-QR-REVIEW",true);
            var mismatchQr = Qr(reviewOrderId,"UI17-QR-MISMATCH",true);
            var firstWait = Qr(waitingOrderId,"UI17-WAIT-ONE",false);
            var secondWait = Qr(secondWaitingOrderId,"UI17-WAIT-TWO",false);
            db.AddRange(matchedQr,reviewQr,mismatchQr,firstWait,secondWait); await db.SaveChangesAsync();
            qrId = firstWait.Id; secondQrId = secondWait.Id;
            AcbQrSession Session(PosPaymentQrRequest qr,string reference,AcbSessionStatus status) => new() {
                StoreId = store.StoreId,QrRequestId = qr.Id,OrderId = qr.OrderId,ShiftId = shift.Id,TerminalId = store.TerminalId,
                CashierId = manager.UserId,ProviderOrderId = reference,TraceNumber = reference+"-TRACE",Amount = 10000,Status = status,
                LastRetrievedAtUtc = DateTime.UtcNow,LastRetrieveJson = JsonSerializer.Serialize(new { secret = Canary }) };
            var matched = Session(matchedQr,"UI17-ACB-MATCHED",AcbSessionStatus.Completed);
            matched.PaymentId = payment.Id; matched.ConfirmationSource = AcbConfirmationSource.Callback;
            matched.ConfirmedAtUtc = DateTime.UtcNow; matched.ConfirmedByUserId = manager.UserId;
            var review = Session(reviewQr,"UI17-ACB-REVIEW",AcbSessionStatus.ReviewRequired);
            review.ReviewReason = "Cần đối chiếu chứng từ ngân hàng gốc trước khi ghi nhận.";
            var mismatch = Session(mismatchQr,"UI17-ACB-MISMATCH",AcbSessionStatus.Pending);
            db.AddRange(matched,review,mismatch); await db.SaveChangesAsync();
            db.AddRange(new AcbPaymentTransaction { StoreId = store.StoreId,SessionId = matched.Id,
                TransactionNumber = "UI17-BANK-TX-MATCHED",Amount = 10000,Status = "COMPLETED",Content = "Khoản đã khớp từ fixture",PostedAt = DateTime.UtcNow.ToString("O") },
                new AcbPaymentTransaction { StoreId = store.StoreId,SessionId = review.Id,
                TransactionNumber = "UI17-BANK-TX-REVIEW",Amount = 9000,Status = "COMPLETED",Content = "Khoản cần đối chiếu",PostedAt = DateTime.UtcNow.ToString("O") });
            var callback = new AcbCallbackReceipt { StoreId = store.StoreId,ClientRequestId = Guid.NewGuid().ToString(),
                RequestCode = "TRANSACTION_UPDATE",Page = 1,TotalPages = 1,PayloadJson = JsonSerializer.Serialize(new { secret = Canary }),
                ProcessedAtUtc = DateTime.UtcNow,NextAttemptAtUtc = DateTime.UtcNow.AddDays(10) };
            foreach (var (reference,amount) in new[] { (matched.ProviderOrderId,10000m),(review.ProviderOrderId,10000m),(mismatch.ProviderOrderId,15000m),("UI17-ACB-UNKNOWN",5000m) })
                callback.Items.Add(new AcbQrNotificationItem { StoreId = store.StoreId,Position = callback.Items.Count,
                    ProviderOrderId = reference,Amount = amount,BusinessDate = businessDate,RequestCode = "TRANSACTION_UPDATE",
                    TransactionStatus = "COMPLETED",DebitOrCredit = "credit",Content = "Dữ liệu callback kiểm thử "+reference });
            db.Add(callback);
            var fingerprint = BankNotificationParser.AccountFingerprint(bank);
            var device = new BankNotificationDevice { StoreId = store.StoreId,BankAccountId = bankId,BankAccountFingerprint = fingerprint,
                Name = "Điện thoại kiểm thử UI17",TokenHash = new string('A',64),LastSeenAtUtc = DateTime.UtcNow,Enabled = true };
            db.Add(device); await db.SaveChangesAsync();
            var receipt = new BankNotificationReceipt { StoreId = store.StoreId,DeviceId = device.Id,BankAccountId = bankId,
                EvidenceKey = new string('B',64),PayloadHash = new string('C',64),Amount = 10000,SenderName = "NGUOI CHUYEN KIEM THU",
                TransactionCode = "UI17-PHONE-UNKNOWN",Content = "Cửa hàng nhận tiền thành công +10.000 VND từ NGUOI CHUYEN KIEM THU tại ACB GD UI17PHONEUNKNOWN MGD: 17001",
                PostedAtUtc = DateTime.UtcNow };
            db.Add(receipt); await db.SaveChangesAsync(); receiptId = receipt.Id;
        }
        var captures = await BankMobileCapturedRazor.CaptureAsync(orderId,store.StoreId,app.Stores[1].StoreId);
        var before = await StateAsync(app);
        foreach (var path in new[] { "/Admin/StoreBankAccounts/Index","/admin/bank-notifications","/admin/acb/settings","/admin/acb/callback-routing","/admin/acb/reconciliation",$"/admin/acb/payments/orders/{orderId}/data" })
        { using var response = await denied.Http.GetAsync(path); Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode); }
        foreach (var path in new[] { "/admin/acb/settings","/admin/acb/callback-routing","/Admin/StoreBankAccounts/Edit?id="+bankId })
        { using var response = await view.Http.GetAsync(path); Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode); }
        using (var response = await staff.Http.GetAsync("/admin/acb/callback-routing")) Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        using (var response = await other.Http.GetAsync($"/admin/acb/payments/orders/{orderId}/data")) Assert.False(response.IsSuccessStatusCode);
        using (var response = await other.Http.GetAsync("/Admin/StoreBankAccounts/Edit?id="+bankId)) Assert.Equal(HttpStatusCode.NotFound,response.StatusCode);
        using (var response = await view.Http.PostAsync("/admin/bank-notifications/approve",new StringContent("{}",System.Text.Encoding.UTF8,"application/json"))) Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        Assert.Equal(before,await StateAsync(app));
        var root = FullApplicationFixture.SourceRoot();
        var output = Environment.GetEnvironmentVariable("GAO_UI17_REVIEW_OUTPUT") ?? MobileAcceptanceEvidence.PathFor(root,"TestResults","mobile-ui-17-20261027","browser");
        Directory.CreateDirectory(output);
        var start = new ProcessStartInfo("node") { WorkingDirectory = root,UseShellExecute = false,CreateNoWindow = true,
            RedirectStandardInput = true,RedirectStandardOutput = true,RedirectStandardError = true };
        start.Environment["NODE_PATH"] = Path.Combine(root,"Logs","pos-offline-browser-deps","node_modules");
        start.Environment["GAO_UI17_REVIEW_OUTPUT"] = output;
        start.ArgumentList.Add(Path.Combine(root,"GaoApp.Tests.Browser","bank-mobile.browser.cjs"));
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { baseUrl = $"http://{store.Host}:{app.Address.Port}",
            terminalId = store.TerminalId,output,manager = new { user = manager.Name,password = manager.Password },
            viewer = new { user = reader.Name,password = reader.Password },orderId,reviewOrderId,waitingOrderId,secondWaitingOrderId,
            bankId,receiptId,qrId,secondQrId,canary = Canary,fromDate = businessDate.ToString("yyyy-MM-dd"),captures }));
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        try { await process.WaitForExitAsync(timeout.Token); } catch { if (!process.HasExited) process.Kill(true); throw; }
        var browserOutput = await stdout+"\n"+await stderr;
        await File.WriteAllTextAsync(Path.Combine(output,"chrome.log"),browserOutput);
        Assert.True(process.ExitCode == 0,browserOutput);
        Assert.Equal(before,await StateAsync(app));
        await using var verify = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(10000m,await verify.OrderPayments.SumAsync(x => x.Amount));
        Assert.Equal(100m,await verify.InventoryBalances.Select(x => x.OnHandQty).SingleAsync());
        Assert.Equal(0,await verify.Set<AcbCallbackReceipt>().SumAsync(x => x.Attempts));
        Assert.Null(await verify.Set<BankNotificationReceipt>().Where(x => x.Id == receiptId).Select(x => x.PaymentId).SingleAsync());
        await File.WriteAllTextAsync(Path.Combine(output,"sql-checks.json"),JsonSerializer.Serialize(new { passed = true,
            paymentTotal = 10000,physicalStock = 100,callbackAttempts = 0,unmatchedReceiptStillUnapplied = true,
            allMoneyOrdersQrEvidenceSettingsSecretsRoutingMenusRolesAndRawPayloadsUnchanged = true,
            noApprovalOrBankCommand = true,acbSettingsEnabled = false,capturedBodiesUseCompiledNativeRazorOnly = true,
            actualRoutingHostListEmpty = true,fixtureOnly = true },new JsonSerializerOptions { WriteIndented = true }));
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
            hostRights = await db.Users.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id,x.IsHostAdmin }).ToListAsync(),
            banks = await Rows<StoreBankAccount>(),settings = await Rows<StoreAcbSettings>(),sessions = await Rows<AcbQrSession>(),
            transactions = await Rows<AcbPaymentTransaction>(),callbacks = await Rows<AcbCallbackReceipt>(),callbackItems = await Rows<AcbQrNotificationItem>(),
            routing = await Rows<AcbCallbackRoute>(),routeChanges = await db.Set<AcbCallbackRouteChange>().AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id,x.RouteId,x.PreviousStoreId,x.TargetStoreId,x.ActorUserId,x.ChangedAtUtc }).ToListAsync(),
            devices = await Rows<BankNotificationDevice>(),notifications = await Rows<BankNotificationReceipt>(),waits = await Rows<BankNotificationWait>(),
            dismissals = await Rows<BankNotificationDismissal>(),qrs = await Rows<PosPaymentQrRequest>(),
            orders = await Rows<Order>(),lines = await Rows<OrderLine>(),payments = await Rows<OrderPayment>(),shifts = await Rows<POSShift>(),
            terminals = await Rows<POSTerminal>(),stock = await Rows<InventoryBalance>(),ledger = await Rows<InventoryTransaction>(),
            products = await Rows<Product>(),variants = await Rows<ProductVariant>(),customers = await Rows<Customer>() });
    }
}
