using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Web.Services.Printing;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class PosPrinterSettingsTests
{
    [Fact]
    public async Task Staff_configures_only_its_local_printer_and_admin_keeps_template_management()
    {
        await using var app=await FullApplicationFixture.StartAsync();var store=app.Stores[0];
        var employee=await app.AddAccountAsync(store,PermissionCodes.Pos.Order.View);
        var admin=await PosShiftAdministrationSqlServerTests.AddAdminAsync(app,store);
        using var staff=await app.LoginAsync(employee);using var manager=await app.LoginAsync(admin);
        using(var anonymous=app.Anonymous(store))using(var response=await anonymous.Http.GetAsync("/admin/pos/printer-settings"))Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
        using(var anonymous=app.Anonymous(store)){
            anonymous.Http.DefaultRequestHeaders.Accept.Clear();anonymous.Http.DefaultRequestHeaders.Accept.ParseAdd("text/html");
            using var response=await anonymous.Http.GetAsync("/admin/pos/printer-settings");Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);Assert.Contains("/admin/account/login",response.Headers.Location!.ToString());
        }
        using(var denied=await app.LoginAsync(await app.AddAccountAsync(store,PermissionCodes.Pos.Order.Reprint)))
        using(var response=await denied.Http.GetAsync("/admin/pos/printer-settings"))Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        using(var response=await staff.Http.GetAsync("/admin/receipt-templates"))Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        var html=await staff.Http.GetStringAsync("/admin/pos/printer-settings");
        Assert.Contains("id=\"applyTemplate\"",html);Assert.DoesNotContain("id=\"designFields\"",html);Assert.DoesNotContain("id=\"saveStoreIdentity\"",html);
        var adminPage=await manager.Http.GetStringAsync("/admin/receipt-templates");
        Assert.Contains("id=\"saveStoreIdentity\"",adminPage);Assert.Contains("id=\"designFields\"",adminPage);Assert.DoesNotContain("id=\"printMode\"",adminPage);
        var initial=await manager.JsonAsync(HttpMethod.Get,"/admin/receipt-templates/default");
        await manager.JsonAsync(HttpMethod.Put,"/admin/receipt-templates/default",new SaveReceiptDefaultRequest("classic-A5",initial.GetProperty("rowVersion").GetString()!,null));
        using(var forbidden=await staff.Http.PutAsJsonAsync("/admin/receipt-templates/default",new SaveReceiptDefaultRequest("classic-A4",initial.GetProperty("rowVersion").GetString()!,null)))Assert.Equal(HttpStatusCode.Forbidden,forbidden.StatusCode);
        var pos=await staff.Http.GetStringAsync("/admin/pos");Assert.DoesNotContain("href=\"/admin/pos/printer-settings\"",pos);
        await using(var db=app.Database.CreateTenantContext(store.StoreId)){
            db.AdminMenuItems.Add(new AdminMenuItem{StoreId=store.StoreId,Title="Máy in nhân viên tự chọn",Url="/admin/pos/printer-settings",PermissionCode=PermissionCodes.Pos.Order.View,IsActive=true,SortOrder=100});await db.SaveChangesAsync();
        }
        pos=WebUtility.HtmlDecode(await staff.Http.GetStringAsync("/admin/pos"));Assert.Contains("href=\"/admin/pos/printer-settings\"",pos);Assert.Contains("Máy in nhân viên tự chọn",pos);
        async Task<string> Protected(){await using var db=app.Database.CreateTenantContext(store.StoreId);return JsonSerializer.Serialize(new{
            settings=await db.Stores.AsNoTracking().Select(x=>new{x.Id,x.ReceiptTemplateKey,x.ReceiptName,x.ReceiptAddress,x.ReceiptPhone,x.RowVersion}).ToArrayAsync(),
            templates=await db.Set<PosReceiptTemplate>().AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.DefinitionJson,x.RowVersion}).ToArrayAsync(),
            menus=await db.AdminMenuItems.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.Title,x.Url,x.PermissionCode,x.RowVersion}).ToArrayAsync(),
            grants=await db.RolePermissions.OrderBy(x=>x.Id).Select(x=>new{x.Id,x.RoleId,x.PermissionId}).ToArrayAsync()});}
        var before=await Protected();var root=FullApplicationFixture.SourceRoot();var output=Path.Combine(root,"Logs","pos-printer-settings-20261101");Directory.CreateDirectory(output);
        var start=new ProcessStartInfo("node"){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add(Path.Combine(root,"GaoApp.Tests.Browser","pos-printer-settings.browser.cjs"));
        using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new{baseUrl=$"http://{store.Host}:{app.Address.Port}",user=employee.Name,password=employee.Password,storeId=store.StoreId,terminalId=store.TerminalId,output}));process.StandardInput.Close();
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));try{await process.WaitForExitAsync(timeout.Token);}catch{if(!process.HasExited)process.Kill(true);throw;}
        var log=await stdout+"\n"+await stderr;await File.WriteAllTextAsync(Path.Combine(output,"browser.log"),log);Assert.True(process.ExitCode==0,log);
        Assert.Equal(before,await Protected());
    }
}
