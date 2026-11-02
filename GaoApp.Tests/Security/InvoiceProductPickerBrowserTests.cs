using System.Diagnostics;
using System.Net;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class InvoiceProductPickerBrowserTests
{
    [Fact]
    public async Task Native_picker_displays_only_eligible_XML_stock_handles_stale_reads_and_preserves_store_rights_and_configuration()
    {
        await using var app=await FullApplicationFixture.StartWithAuditPausedAsync();
        var store=app.Stores[0];var manager=await app.AddAccountAsync(store,"*");
        var denied=await app.AddAccountAsync(store,PermissionCodes.Pos.Order.View);var foreign=await app.AddAccountAsync(app.Stores[1],"*");
        var orderId=await InvoiceOrderDraftSqlServerTests.SeedOrder(app,manager,"PICKER-BROWSER",InvoiceIssuanceRoute.Manual);
        int invoiceId,positiveId,fractionalId,emptyId,correctionId;
        await using(var db=app.Database.CreateTenantContext(store.StoreId))
        {
            var warehouse=await db.Warehouses.SingleAsync(x=>x.Id==store.WarehouseId);warehouse.Name="Kho chính";
            var legal=await db.LegalEntities.SingleAsync(x=>x.Id==warehouse.LegalEntityId);legal.DefaultWarehouseId=warehouse.Id;
            var original=await db.ProductVariants.Include(x=>x.Product).SingleAsync(x=>x.Id==store.VariantId);
            var positive=new ProductVariant{StoreId=store.StoreId,ProductId=original.ProductId,Sku="HOA-25",ProductVariantName="1 cành hồng hoa giả",ProductVariantNameNormalized="1 canh hong hoa gia",Price=28000,IsActive=true};
            var fractional=new ProductVariant{StoreId=store.StoreId,ProductId=original.ProductId,Sku="HOA-26",ProductVariantName="Hoa lẻ",ProductVariantNameNormalized="hoa le",Price=8000,IsActive=true};
            var empty=new ProductVariant{StoreId=store.StoreId,ProductId=original.ProductId,Sku="HOA-00",ProductVariantName="Hoa hết tồn",ProductVariantNameNormalized="hoa het ton",Price=9000,IsActive=true};
            var unit=await db.Units.SingleAsync(x=>x.Id==original.Product.BaseUnitId);unit.Name="Cái";
            var head=new InvoiceHead{StoreId=store.StoreId,OrderId=orderId,LegalEntityId=legal.Id,ProviderStatus=InvoiceProviderStatus.LocalDraft,InvoiceNumber="PICKER-DEMO"};
            db.AddRange(positive,fractional,empty,head);await db.SaveChangesAsync();positiveId=positive.Id;fractionalId=fractional.Id;emptyId=empty.Id;invoiceId=head.Id;
            foreach(var data in new[]{(positiveId,12m),(fractionalId,.5m)})db.Add(new InvoiceInputStockSupplementalMovement{StoreId=store.StoreId,WarehouseId=warehouse.Id,ProductVariantId=data.Item1,
                QuantityChange=data.Item2,EffectiveAtUtc=DateTime.UtcNow,MovementType=InvoiceInputStockSupplementalMovementType.LegacyOpening,LegacySourceKey="PICKER-BROWSER-"+data.Item1});
            db.Add(new InvoiceDetail{StoreId=store.StoreId,InvoiceHeadId=head.Id,ProductVariantId=positive.Id,ItemName=positive.ProductVariantName!,UnitName="Cái",Quantity=2,UnitPrice=28000,
                Amount=56000,TotalAmount=56000,SourceType=InvoiceDetailSourceType.Manual});
            head.TotalQuantity=2;head.SubTotal=56000;head.GrandTotal=56000;
            var correction=new InvoiceHead{StoreId=store.StoreId,LegalEntityId=legal.Id,IsAutoInvoiceGroup=true,OriginalInvoiceHeadId=head.Id,CorrectionType=InvoiceCorrectionType.AdjustmentAmount};
            db.Add(correction);await db.SaveChangesAsync();correctionId=correction.Id;
        }
        async Task<string> ProtectedState()
        {
            await using var db=app.Database.CreateHostContext();
            async Task<object> Rows<T>()where T:class { var rows=await db.Set<T>().IgnoreQueryFilters().AsNoTracking().ToListAsync();return rows.Select(row=>db.Entry(row).Properties.OrderBy(p=>p.Metadata.Name).ToDictionary(p=>p.Metadata.Name,p=>p.CurrentValue)).ToArray(); }
            return JsonSerializer.Serialize(new{stores=await Rows<Store>(),menus=await Rows<AdminMenuItem>(),pins=await Rows<AdminMenuPinSetting>(),visibility=await Rows<AdminMenuVisibilitySetting>(),
                roles=await Rows<Role>(),grants=await Rows<RolePermission>(),warehouses=await Rows<Warehouse>(),legal=await Rows<LegalEntity>(),providers=await Rows<InvoiceProviderSetting>(),
                orders=await Rows<Order>(),lines=await Rows<OrderLine>(),payments=await Rows<OrderPayment>(),stock=await Rows<InventoryBalance>(),ledger=await Rows<InventoryTransaction>(),xml=await Rows<InvoiceInputStockSupplementalMovement>(),
                automatic=await Rows<AutoInvoiceSettings>(),operations=await Rows<AutoInvoiceOperation>(),rewards=await Rows<RewardSettings>(),vouchers=await Rows<CustomerRewardVoucher>()});
        }
        var before=await ProtectedState();
        using(var client=await app.LoginAsync(denied))foreach(var path in new[]{$"/Admin/Invoice/SearchProductVariants?invoiceHeadId={invoiceId}&keyword=hoa",$"/Admin/Invoice/ProductStock?invoiceHeadId={invoiceId}&productVariantId={positiveId}"}){
            using var response=await client.Http.GetAsync(path);Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);}
        using(var client=await app.LoginAsync(foreign))foreach(var path in new[]{$"/Admin/Invoice/SearchProductVariants?invoiceHeadId={invoiceId}&keyword=hoa",$"/Admin/Invoice/ProductStock?invoiceHeadId={invoiceId}&productVariantId={positiveId}"}){
            using var response=await client.Http.GetAsync(path);Assert.Equal(HttpStatusCode.NotFound,response.StatusCode);}
        var root=FullApplicationFixture.SourceRoot();var output=Path.Combine(root,"TestResults","invoice-product-picker-20261029");Directory.CreateDirectory(output);
        var start=new ProcessStartInfo("node"){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.Environment["NODE_PATH"]=Path.Combine(root,"Logs","pos-offline-browser-deps","node_modules");start.ArgumentList.Add(Path.Combine(root,"GaoApp.Tests.Browser","invoice-product-picker.browser.cjs"));
        using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new{baseUrl=$"http://{store.Host}:{app.Address.Port}",terminalId=store.TerminalId,user=manager.Name,password=manager.Password,
            invoiceId,positiveId,fractionalId,emptyId,correctionId,output}));process.StandardInput.Close();
        try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(6));}catch{if(!process.HasExited)process.Kill(true);throw;}
        var log=await stdout+"\n"+await stderr;await File.WriteAllTextAsync(Path.Combine(output,"chrome.log"),log);Assert.True(process.ExitCode==0,log);
        Assert.Equal(before,await ProtectedState());
        await using(var db=app.Database.CreateTenantContext(store.StoreId))
        {
            Assert.Equal(2,await db.InvoiceDetails.CountAsync(x=>x.InvoiceHeadId==invoiceId));
            var head=await db.InvoiceHeads.SingleAsync(x=>x.Id==invoiceId);Assert.Equal(3m,head.TotalQuantity);Assert.Equal(84000m,head.GrandTotal);
            Assert.False(head.IsLocked);Assert.Equal(InvoiceProviderStatus.LocalDraft,head.ProviderStatus);
            Assert.False(db.Database.HasPendingModelChanges());Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        }
        await File.WriteAllTextAsync(Path.Combine(output,"sql-checks.json"),JsonSerializer.Serialize(new{passed=true,onlyExpectedManualLineAdded=true,quantity=3,total=84000,
            configurationMenusRolesProvidersOrdersPhysicalAndDocumentaryStockUnchanged=true,noPendingMigrations=true,fixtureOnly=true,existingChromeWindowsPreserved=true},new JsonSerializerOptions{WriteIndented=true}));
    }
}
