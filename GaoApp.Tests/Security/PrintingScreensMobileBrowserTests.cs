using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Common;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Tests.Delivery;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class PrintingScreensMobileBrowserTests
{
    [Fact]
    public async Task Native_UI22_customer_display_cart_local_QR_confirmation_and_disconnect_use_committed_data()
    {
        await using var app=await FullApplicationFixture.StartWithAuditPausedAsync();var store=app.Stores[0];
        var account=await app.AddAccountAsync(store,"*");
        await using(var db=app.Database.CreateTenantContext(store.StoreId)){
            db.POSShifts.Add(new POSShift{StoreId=store.StoreId,TerminalId=store.TerminalId,WarehouseId=store.WarehouseId,OpenedByUserId=account.UserId,Status=POSShiftStatus.Open});
            db.StoreBankAccounts.Add(new StoreBankAccount{StoreId=store.StoreId,BankCode="ACB",BankName="ACB",AccountNumber="123456789",AccountName="Disposable local QR fixture",VietQrBankBin="970416",IsDefault=true,IsActive=true,QrRenderMode=BankQrRenderMode.LocalEmvQr,ConfirmMode=BankQrConfirmMode.Manual});
            await db.SaveChangesAsync();
        }
        var root=FullApplicationFixture.SourceRoot();var output=MobileAcceptanceEvidence.PathFor(root,"TestResults","mobile-ui-22-20261028","customer-display");Directory.CreateDirectory(output);
        var start=new ProcessStartInfo("node"){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.Environment["NODE_PATH"]=Path.Combine(root,"Logs","pos-offline-browser-deps","node_modules");start.ArgumentList.Add(Path.Combine(root,"GaoApp.Tests.Browser","printing-customer-display.browser.cjs"));
        using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new{baseUrl=$"http://{store.Host}:{app.Address.Port}",storeId=store.StoreId,terminalId=store.TerminalId,variantId=store.VariantId,user=account.Name,password=account.Password,output}));process.StandardInput.Close();
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(8));try{await process.WaitForExitAsync(timeout.Token);}catch{if(!process.HasExited)process.Kill(true);throw;}
        var log=await stdout+"\n"+await stderr;await File.WriteAllTextAsync(Path.Combine(output,"chrome.log"),log);Assert.True(process.ExitCode==0,log);
        await using var check=app.Database.CreateTenantContext(store.StoreId);
        var order=await check.Orders.Include(x=>x.Payments).SingleAsync(x=>x.Status==OrderStatus.Completed);Assert.Equal(OrderStatus.Completed,order.Status);Assert.Equal(order.GrandTotal,order.Payments.Sum(x=>x.Amount));
        Assert.Single(await check.PosPaymentQrRequests.ToListAsync());Assert.Empty(await check.Set<StoreAcbSettings>().ToListAsync());
        await File.WriteAllTextAsync(Path.Combine(output,"sql-checks.json"),JsonSerializer.Serialize(new{passed=true,localQrOnly=true,externalBankCredentials=false,externalBankCalls=false,committedOrderId=order.Id,order.GrandTotal,confirmedPayments=order.Payments.Count}));
    }

    [Fact]
    public async Task Native_UI22_printing_and_dedicated_screens_keep_paper_data_permissions_and_store_configuration()
    {
        await using var app = await FullApplicationFixture.StartWithAuditPausedAsync();
        var root=FullApplicationFixture.SourceRoot();var output=MobileAcceptanceEvidence.PathFor(root,"TestResults","mobile-ui-22-20261028","browser");Directory.CreateDirectory(output);
        var store=app.Stores[0];
        var admin=await SecurityAdministrationPlanATests.MakeAdmin(app,await app.AddAccountAsync(store,"*"));
        var monitor=await app.AddAccountAsync(store,PermissionCodes.Admin.StoreMonitorView);
        var reader=await app.AddAccountAsync(store,PermissionCodes.Pos.Order.View,PermissionCodes.Pos.Order.Reprint,PermissionCodes.System.ProductLabel.Print);
        var denied=await app.AddAccountAsync(store,PermissionCodes.Catalog.Customer.View);
        var kiosk=await KioskSqlServerTests.SeedAsync(app,store,admin.UserId);
        using var delivery=await DeliveryBillPresentationSeed.CreateAsync(app);
        int purchaseId,closingId,handoverId,creditId;
        await using(var db=app.Database.CreateTenantContext(store.StoreId)){
            (await db.Stores.SingleAsync(x=>x.Id==store.StoreId)).Name="R2 Inventory Posting Store";
            var variant=await db.ProductVariants.SingleAsync(x=>x.Id==store.VariantId);var shift=await db.POSShifts.SingleAsync(x=>x.TerminalId==store.TerminalId);
            var sale=await db.Orders.SingleAsync(x=>x.Id==kiosk.OrderId);sale.OrderNumber="UI22-SALE-100000";sale.CompletedAtUtc=DateTime.UtcNow;sale.Subtotal=100000;
            for(var i=1;i<=20;i++)sale.Lines.Add(new OrderLine{StoreId=store.StoreId,ProductId=variant.ProductId,VariantId=variant.Id,
                ItemName=$"UI22 Sản phẩm tiếng Việt tên dài · dòng {i}",Quantity=1,BaseQuantity=1,Multiplier=1,UnitName="Chai",SellingUnitName="Chai",UnitPrice=5000,OriginalUnitPrice=5000,LineTotal=5000,Barcode="8934588063053"});
            var credit=new Order{StoreId=store.StoreId,CustomerId=kiosk.CustomerId,POSShiftId=shift.Id,OrderNumber="UI22-DEBT",Status=OrderStatus.Completed,
                IsCreditSale=true,Subtotal=60000,GrandTotal=60000,BalanceDue=60000,PaymentStatus=PaymentStatus.Unpaid,CompletedAtUtc=DateTime.UtcNow};
            db.Add(credit);(await db.Customers.SingleAsync(x=>x.Id==kiosk.CustomerId)).HaveDebt=true;
            var entity=await db.LegalEntities.FirstAsync();var supplier=await db.Suppliers.FirstAsync();
            var purchase=new PurchaseOrder{StoreId=store.StoreId,OrderNumber="UI22-PO-A4",ExpectedWarehouseId=store.WarehouseId,LegalEntityId=entity.Id,SupplierId=supplier.Id,
                Title="Đơn đặt hàng kiểm thử UI22",Status=PurchaseOrderStatus.Draft,SubtotalBeforeVat=140000,TotalAfterVat=140000,OutsideRequestReason="Fixture riêng"};
            for(var i=1;i<=28;i++)purchase.Lines.Add(new PurchaseOrderLine{StoreId=store.StoreId,LineNo=i,ProductVariantId=variant.Id,ProductNameSnapshot=$"UI22 Đơn mua · mặt hàng tên dài {i}",UnitNameSnapshot="Chai",OrderedQuantity=1,ConversionFactor=1,UnitPriceBeforeVat=5000,UnitPriceAfterVat=5000,LineTotalBeforeVat=5000,LineTotalAfterVat=5000});
            var closing=new POSShiftClosingSlip{StoreId=store.StoreId,POSShiftId=shift.Id,SlipCode="UI22-CLOSE",BarcodeValue="UI22-CLOSE-80",OpenedByUserId=admin.UserId,ClosedByUserId=admin.UserId,
                OpenedAtUtc=DateTime.UtcNow.AddHours(-4),ClosedAtUtc=DateTime.UtcNow,OpeningCash=100000,ClosingCashActual=350000,ClosingCashExpected=350000};
            closing.Denominations.Add(new POSShiftClosingSlipDenomination{StoreId=store.StoreId,DenominationValue=50000,Quantity=7,Amount=350000});
            var handover=new POSShiftHandoverSlip{StoreId=store.StoreId,SlipCode="UI22-HANDOVER",BarcodeValue="UI22-HANDOVER-80",TerminalId=store.TerminalId,WarehouseId=store.WarehouseId,CreatedByUserId=admin.UserId,OpeningCashTotal=100000};
            handover.Denominations.Add(new POSShiftHandoverSlipDenomination{StoreId=store.StoreId,DenominationValue=50000,Quantity=2,Amount=100000});
            db.AddRange(purchase,closing,handover);await db.SaveChangesAsync();
            db.Add(new CustomerReceivableEntry{StoreId=store.StoreId,CustomerId=kiosk.CustomerId,OrderId=credit.Id,Kind="Sale",Amount=60000});await db.SaveChangesAsync();
            purchaseId=purchase.Id;closingId=closing.Id;handoverId=handover.Id;creditId=credit.Id;
        }
        using var client=await app.LoginAsync(admin);
        var deposit=await client.JsonAsync(HttpMethod.Post,"/admin/customer-deposit/receive",new{clientRequestId=Guid.NewGuid(),customerId=kiosk.CustomerId,amount=250000,method=0,purpose="UI22 phiếu nhận cọc · nội dung đã lưu"});
        var collected=await client.JsonAsync(HttpMethod.Post,"/admin/customer-debt/collect",new{clientRequestId=Guid.NewGuid(),customerId=kiosk.CustomerId,orderId=creditId,amount=10000,method=0,note="UI22 thu công nợ"});
        await client.Http.GetStringAsync("/admin/label-printing/templates");
        var before=await Protected(app);
        using(var blocked=await app.LoginAsync(denied)){
            foreach(var route in new[]{"/admin/label-printing","/admin/store-monitor","/admin/pos/customer-display"}){
                using var response=await blocked.Http.GetAsync(route);Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
            }
            using var update=await blocked.Http.PutAsJsonAsync("/admin/receipt-templates/default",new{templateKey="classic-A4"});
            Assert.Equal(HttpStatusCode.Forbidden,update.StatusCode);
        }
        var web=TestApplicationBuild.WebAssemblyPath();var compiled=new Microsoft.AspNetCore.Razor.Hosting.RazorCompiledItemLoader().LoadItems(System.Reflection.Assembly.LoadFrom(web)).Select(x=>x.Identifier).ToHashSet();
        using var inventory=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root,"docs/development/mobile-ui-20261025/view-inventory.json")));
        var views=inventory.RootElement.GetProperty("views").EnumerateArray().Where(v=>v.GetProperty("step").GetString()=="UI-22").Select(v=>v.GetProperty("path").GetString()!.Replace("GaoApp.Web","")).ToArray();Assert.Equal(16,views.Length);Assert.All(views,v=>Assert.Contains(v,compiled));
        foreach(var asset in new[]{"Admin/css/print-workspace.mobile.css","Admin/js/print-workspace.mobile.js","Admin/css/print-page.mobile.css","Admin/js/print-page.mobile.js","Admin/css/special-displays.mobile.css","Admin/js/special-displays.mobile.js"})
            Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(root,"GaoApp.Web/wwwroot",asset)),await client.Http.GetByteArrayAsync('/'+asset));
        await File.WriteAllTextAsync(Path.Combine(output,"compiled-views.json"),JsonSerializer.Serialize(new{passed=true,views,webAssembly=web,sha256=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(web))).ToLowerInvariant(),nativeAssetsMatch=true}));
        object Account(FullApplicationFixture.Account a)=>new{user=a.Name,password=a.Password};
        await Node("printing-screens-mobile.browser.cjs",new{baseUrl=$"http://{store.Host}:{app.Address.Port}",storeId=store.StoreId,terminalId=store.TerminalId,variantId=store.VariantId,orderId=kiosk.OrderId,output,
            admin=Account(admin),reader=Account(reader),monitor=Account(monitor),kioskKey=kiosk.Key,kioskBarcode=kiosk.Barcode,purchaseId,closingId,handoverId,deliveryUrl=delivery.BillUrl,
            depositId=deposit.GetProperty("depositId").GetInt32(),debtReceiptId=collected.GetProperty("receiptId").GetInt32()},"chrome.log");
        Assert.Equal(before,await Protected(app));
        await using(var db=app.Database.CreateHostContext()){Assert.False(db.Database.HasPendingModelChanges());Assert.Empty(await db.Database.GetPendingMigrationsAsync());}
        await File.WriteAllTextAsync(Path.Combine(output,"sql-checks.json"),JsonSerializer.Serialize(new{passed=true,strictMigrations=true,beforeSha256=Hash(before),afterSha256=Hash(await Protected(app)),
            storeMenusGrantsConfigurationAndExistingFinancialDataUnchanged=true,physicalPrintersUsed=false,externalPaymentCalls=false,existingChromeWindowsPreserved=true}));
        async Task Node(string script,object input,string logName){
            var start=new ProcessStartInfo("node"){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
            start.Environment["NODE_PATH"]=Path.Combine(root,"Logs","pos-offline-browser-deps","node_modules");start.ArgumentList.Add(Path.Combine(root,"GaoApp.Tests.Browser",script));
            using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(input));process.StandardInput.Close();
            using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(18));try{await process.WaitForExitAsync(timeout.Token);}catch{if(!process.HasExited)process.Kill(true);throw;}
            var log=await stdout+"\n"+await stderr;await File.WriteAllTextAsync(Path.Combine(output,logName),log);Assert.True(process.ExitCode==0,log);
        }
    }
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static async Task<string> Protected(FullApplicationFixture app){
        await using var db=app.Database.CreateHostContext();
        async Task<object> Rows<T>()where T:BaseEntity{var rows=await db.Set<T>().IgnoreQueryFilters().AsNoTracking().OrderBy(x=>x.Id).ToListAsync();return rows.Select(row=>db.Entry(row).Properties.OrderBy(p=>p.Metadata.Name).ToDictionary(p=>p.Metadata.Name,p=>p.CurrentValue)).ToArray();}
        return JsonSerializer.Serialize(new{stores=await Rows<Store>(),menus=await Rows<AdminMenuItem>(),roles=await Rows<Role>(),grants=await db.RolePermissions.OrderBy(x=>x.Id).Select(x=>new{x.Id,x.RoleId,x.PermissionId}).ToArrayAsync(),
            payments=await Rows<OrderPayment>(),stock=await Rows<InventoryBalance>(),deposits=await Rows<CustomerDeposit>(),entries=await Rows<CustomerDepositEntry>(),debts=await Rows<CustomerDebtReceipt>(),purchase=await Rows<PurchaseOrder>(),
            receiptDefaults=await db.Stores.OrderBy(x=>x.Id).Select(x=>new{x.Id,x.ReceiptName,x.ReceiptAddress,x.ReceiptPhone}).ToArrayAsync()});
    }
}
