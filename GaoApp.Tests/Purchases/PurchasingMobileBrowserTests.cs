using System.Diagnostics;
using System.Net;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Tests.Security;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Purchases;

[Collection("R1FinalDatabasePreflight")]
public sealed class PurchasingMobileBrowserTests
{
    [Fact]
    public async Task Phone_purchase_flow_preserves_approved_units_source_native_forms_receiving_lease_and_store_configuration()
    {
        await using var app = await FullApplicationFixture.StartPurchasingAsync(); var store = app.Stores[0];
        var manager = await app.AddAccountAsync(store, "*");
        var viewer = await app.AddAccountAsync(store, PermissionCodes.Purchase.Request.ViewStore, PermissionCodes.Purchase.Order.View, PermissionCodes.Purchase.Receipt.View);
        var denied = await app.AddAccountAsync(store, PermissionCodes.Admin.DashboardView);
        var foreign = await app.AddAccountAsync(app.Stores[1], "*");
        int baseConversion, boxConversion, supplier, legal; string productName;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var variant = await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == store.VariantId);
            variant.Product.Name = "Sản phẩm kiểm thử mua hàng nhiều đơn vị, đối chiếu nguyên kiện và hạn sử dụng";
            productName = variant.Product.Name;
            var box = new Unit { StoreId = store.StoreId, Code = "UI10-BOX", Name = "Hộp sáu", IsActive = true };
            db.Units.Add(box); await db.SaveChangesAsync();
            var one = new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = variant.Id, UnitId = variant.Product.BaseUnitId, Factor = 1, IsBaseUnit = true, IsActive = true };
            var six = new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = variant.Id, UnitId = box.Id, Factor = 6, IsActive = true };
            db.ProductUnitConversions.AddRange(one, six); await db.SaveChangesAsync(); baseConversion = one.Id; boxConversion = six.Id;
            supplier = await db.Suppliers.Select(x => x.Id).FirstAsync(); legal = (await db.Warehouses.SingleAsync(x => x.Id == store.WarehouseId)).LegalEntityId;
        }
        using var actor = await app.LoginAsync(manager);
        var form = new Dictionary<string,string> { ["Title"]="UI-10 yêu cầu mua thử riêng", ["Note"]="Đối chiếu nguồn và giữ đúng số lượng",
            ["Lines[0].ItemKind"]="1",["Lines[0].ProductVariantId"]=store.VariantId.ToString(),["Lines[0].ProductUnitConversionId"]=baseConversion.ToString(),["Lines[0].Quantity"]="2.5",["Lines[0].Note"]="Chỉ nhận hàng nguyên tem",
            ["Lines[1].ItemKind"]="1",["Lines[1].ProductVariantId"]=store.VariantId.ToString(),["Lines[1].ProductUnitConversionId"]=boxConversion.ToString(),["Lines[1].Quantity"]="3",["Lines[1].Note"]="Hộp sáu, giữ riêng đơn vị" };
        using(var saved=await actor.Http.PostAsync("/admin/purchase-requests/save",new FormUrlEncodedContent(form))) Assert.Equal(HttpStatusCode.Redirect,saved.StatusCode);
        int requestId;
        await using(var db=app.Database.CreateTenantContext(store.StoreId)) requestId=await db.PurchaseRequests.Select(x=>x.Id).SingleAsync();
        var configuration=await PosCheckoutMobileBrowserFixture.FingerprintAsync(app,store);
        var root=FullApplicationFixture.SourceRoot();var output=Path.Combine(root,"TestResults","mobile-ui-10-20261026","browser");Directory.CreateDirectory(output);
        object Account(FullApplicationFixture.Account a)=>new {name=a.Name,password=a.Password,terminalId=a.Store.TerminalId,baseUrl=$"http://{a.Store.Host}:{app.Address.Port}"};
        var start=new ProcessStartInfo("node"){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.Environment["NODE_PATH"]=Path.Combine(root,"Logs","pos-offline-browser-deps","node_modules");
        start.ArgumentList.Add(Path.Combine(root,"GaoApp.Tests.Browser","purchasing.mobile.browser.cjs"));
        using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {manager=Account(manager),viewer=Account(viewer),denied=Account(denied),foreign=Account(foreign),requestId,baseConversion,boxConversion,supplier,legal,warehouse=store.WarehouseId,productName,output}));process.StandardInput.Close();
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(12));try{await process.WaitForExitAsync(timeout.Token);}catch{if(!process.HasExited)process.Kill(true);throw;}
        await File.WriteAllTextAsync(Path.Combine(output,"browser.log"),await stdout+await stderr);Assert.True(process.ExitCode==0,await stdout+await stderr);
        Assert.Equal(configuration,await PosCheckoutMobileBrowserFixture.FingerprintAsync(app,store));
        await using var proof=app.Database.CreateTenantContext(store.StoreId);
        var request=await proof.PurchaseRequests.Include(x=>x.Lines).SingleAsync(x=>x.Id==requestId);
        var order=await proof.PurchaseOrders.Include(x=>x.Lines).SingleAsync(x=>x.SourcePurchaseRequestId==requestId);
        Assert.Equal(PurchaseRequestStatus.Converted,request.Status);
        Assert.Equal(2,order.Lines.Count);Assert.False(order.HasVat);Assert.Equal(supplier,order.SupplierId);Assert.Equal(legal,order.LegalEntityId);Assert.Equal(store.WarehouseId,order.ExpectedWarehouseId);
        foreach(var line in order.Lines){var source=request.Lines.Single(x=>x.Id==line.SourcePurchaseRequestLineId);Assert.Equal(source.ApprovedQuantity,line.OrderedQuantity);Assert.Equal(source.ConversionFactor,line.ConversionFactor);Assert.Equal(source.Note,line.Note);Assert.Equal(0,line.ReceivedQuantity);Assert.Equal(0,line.ShortClosedQuantity);}
        var receipt=await proof.StockDocuments.Include(x=>x.Lines).SingleAsync(x=>x.PurchaseOrderId==order.Id);
        Assert.Equal(StockDocumentStatus.PendingApproval,receipt.Status);
        var received=Assert.Single(receipt.Lines);Assert.Equal(boxConversion,received.ProductUnitConversionId);Assert.Equal(1.25m,received.Quantity);Assert.Equal(6,received.Factor);Assert.Equal(7.5m,received.BaseQuantity);Assert.Equal(order.Lines.Single(x=>x.ProductUnitConversionId==boxConversion).Id,received.PurchaseOrderLineId);
        Assert.Equal(100,(await proof.InventoryBalances.SingleAsync(x=>x.WarehouseId==store.WarehouseId&&x.ProductVariantId==store.VariantId)).OnHandQty);
        Assert.Equal(1,await proof.PurchaseOrders.CountAsync(x=>x.SourcePurchaseRequestId==requestId));
        await File.WriteAllTextAsync(Path.Combine(output,"sql-effects.json"),JsonSerializer.Serialize(new {passed=true,requestId,orderId=order.Id,receiptId=receipt.Id,received.Quantity,received.Factor,received.BaseQuantity,sourceLines=2,duplicateSourceOrders=0,stockUnchanged=100,receiptPendingManagerApproval=true,configuredMenusPermissionsAndStoreConfigurationUnchanged=true},new JsonSerializerOptions{WriteIndented=true}));
    }
}
