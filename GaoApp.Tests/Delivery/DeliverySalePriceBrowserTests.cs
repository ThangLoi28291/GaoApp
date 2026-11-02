using System.Diagnostics;
using System.Text.Json;
using GaoApp.Tests.Security;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Delivery;

[Collection("DeliveryD08")]
public sealed class DeliverySalePriceBrowserTests(DeliveryD02Fixture fixture)
{
    [Fact]
    public async Task Desktop_and_phone_manage_prices_after_dispatch_and_retry_a_lost_response_without_repeating_changes()
    {
        using var c=await DeliveryD08Case.CreateAsync(fixture);
        var root=FullApplicationFixture.SourceRoot();var output=Path.Combine(root,"Logs","delivery-sale-price-20261101","browser");Directory.CreateDirectory(output);
        var start=new ProcessStartInfo("node") {WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,
            RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add(Path.Combine(root,"GaoApp.Tests.Browser","delivery-sale-price.browser.cjs"));
        using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new{baseUrl=$"http://{c.Closer.Store.Host}:{fixture.Web.Address.Port}",
            user=c.Closer.Name,password=c.Closer.Password,terminal=c.Closer.Store.TerminalId,orderId=c.Id,output}));process.StandardInput.Close();
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try{await process.WaitForExitAsync(timeout.Token);}catch{if(!process.HasExited)process.Kill(true);throw;}
        var log=await stdout+"\n"+await stderr;await File.WriteAllTextAsync(Path.Combine(output,"browser.log"),log);Assert.True(process.ExitCode==0,log);
        await using var db=c.Context();
        Assert.Equal(2,await db.DeliveryRevisions.CountAsync(x=>x.DeliveryOrderId==c.Id&&x.Action=="sale-price-change"));
        Assert.Empty(await db.DeliveryJournalEntries.Where(x=>x.DeliveryOrderId==c.Id&&x.Kind==GaoApp.Domain.Delivery.DeliveryJournalKind.Return).ToArrayAsync());
    }
}
