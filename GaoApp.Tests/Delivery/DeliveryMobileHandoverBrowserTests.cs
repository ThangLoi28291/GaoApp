using System.Diagnostics;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Enums;
using GaoApp.Tests.Security;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Delivery;

[Collection("DeliveryD08")]
public sealed class DeliveryMobileHandoverBrowserTests(DeliveryD02Fixture fixture)
{
    [Fact]
    public async Task Phone_handover_preserves_source_partial_goods_exact_retry_current_counter_and_completed_bill()
    {
        using var c = await DeliveryD08Case.CreateAsync(fixture);
        var view = await fixture.Web.AddAccountAsync(c.Closer.Store,PermissionCodes.Delivery.View);
        var denied = await fixture.Web.AddAccountAsync(c.Closer.Store,PermissionCodes.Admin.DashboardView);
        var foreign = await fixture.Web.AddAccountAsync(fixture.Web.Stores[1],"*");
        var configuration = await PosCheckoutMobileBrowserFixture.FingerprintAsync(fixture.Web,c.Closer.Store);
        var originalSource = await c.Legacy.SourceAsync(); var originalDispatch = await c.DispatchProofAsync(); var originalCart = await c.ClosingCartAsync();
        var root = FullApplicationFixture.SourceRoot(); var output = Path.Combine(root,"TestResults","mobile-ui-09-20261026","handover");Directory.CreateDirectory(output);
        object Account(FullApplicationFixture.Account a) => new {name=a.Name,password=a.Password,terminalId=a.Store.TerminalId,baseUrl=$"http://{a.Store.Host}:{fixture.Web.Address.Port}"};
        var start = new ProcessStartInfo("node") {WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.Environment["NODE_PATH"]=Path.Combine(root,"Logs","pos-offline-browser-deps","node_modules");start.Environment["GAO_DELIVERY_MOBILE_REVIEW"]="1";
        start.ArgumentList.Add(Path.Combine(root,"GaoApp.Tests.Browser","delivery-handover.mobile.browser.cjs"));
        using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {manager=Account(c.Closer),view=Account(view),denied=Account(denied),foreign=Account(foreign),tripId=c.Trip.TripId,firstOrderId=c.Id,secondOrderId=c.Legacy.Id(1),output}));process.StandardInput.Close();
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(8));try{await process.WaitForExitAsync(timeout.Token);}catch{if(!process.HasExited)process.Kill(true);throw;}
        await File.WriteAllTextAsync(Path.Combine(output,"browser.log"),await stdout+await stderr);Assert.True(process.ExitCode==0,await stdout+await stderr);
        Assert.Equal(configuration,await PosCheckoutMobileBrowserFixture.FingerprintAsync(fixture.Web,c.Closer.Store));
        Assert.Equal(originalSource,await c.Legacy.SourceAsync());Assert.Equal(originalDispatch,await c.DispatchProofAsync());Assert.Equal(originalCart,await c.ClosingCartAsync());
        var workspace=await c.WorkspaceAsync();Assert.True(workspace.Trip.IsCompleted);Assert.All(workspace.Orders,x=>Assert.NotNull(x.Settlement));
        var first=workspace.Orders.Single(x=>x.DeliveryOrderId==c.Id).Settlement!;var second=workspace.Orders.Single(x=>x.DeliveryOrderId==c.Legacy.Id(1)).Settlement!;
        Assert.Equal("50000",first.PayableText);Assert.NotNull(first.SaleOrderId);Assert.Null(second.SaleOrderId);
        await using var db=c.Context();var sale=await db.Orders.Include(x=>x.Lines).Include(x=>x.Payments).SingleAsync(x=>x.Id==first.SaleOrderId);
        Assert.Equal(OrderStatus.Completed,sale.Status);Assert.Equal(50000,sale.GrandTotal);Assert.Equal(50000,sale.PaidTotal);Assert.Equal(2.5m,sale.Lines.Sum(x=>x.BaseQuantity));Assert.Equal(22500,sale.Lines.Sum(x=>x.LineCostTotal));
        Assert.Equal(50000,Assert.Single(sale.Payments).Amount);Assert.Equal(PaymentMethod.Cash,Assert.Single(sale.Payments).Method);Assert.Equal(c.ShiftId,sale.POSShiftId);
        var ids=new[]{c.Id,c.Legacy.Id(1)};var returned=await db.DeliveryReturnedCostFragments.Where(x=>ids.Contains(x.DeliveryOrderId)).ToArrayAsync();Assert.Equal(4.5m,returned.Sum(x=>x.BaseQuantity));Assert.Equal(46500,returned.Sum(x=>x.CostAmount));
        Assert.Equal(2,await db.DeliveryRevisions.CountAsync(x=>ids.Contains(x.DeliveryOrderId)&&x.Action=="counter-handover"));
        Assert.Equal(0,await db.InventoryTransactions.CountAsync(x=>x.ReferenceType==InventoryReferenceType.Order&&x.ReferenceId==sale.Id.ToString()));
        var shift=await db.POSShifts.SingleAsync(x=>x.Id==c.ShiftId);Assert.Equal(50000,shift.CashSalesTotal);Assert.Equal(c.CurrentCartId,shift.CurrentOrderId);
        await File.WriteAllTextAsync(Path.Combine(output,"sql-effects.json"),JsonSerializer.Serialize(new {passed=true,saleId=sale.Id,sale.GrandTotal,sale.PaidTotal,sale.POSShiftId,deliveredBaseQuantity=2.5m,returnedBaseQuantity=4.5m,returnedCost=46500,deliveredCost=22500,counterConfirmations=2,payments=1,duplicateSaleStockMoves=0,sourceAndDispatchAndCurrentCartAndMenusStoreConfigurationUnchanged=true,tripCompleted=true},new JsonSerializerOptions{WriteIndented=true}));
    }
}
