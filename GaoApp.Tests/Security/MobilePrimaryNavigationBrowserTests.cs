using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Common;
using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class MobilePrimaryNavigationBrowserTests
{
    [Fact]
    public async Task Preferred_phone_tabs_render_without_pins_and_preserve_desktop_store_and_role_configuration()
    {
        await using var app=await FullApplicationFixture.StartWithAuditPausedAsync();var store=app.Stores[0];
        var full=await app.AddAccountAsync(store,"*");
        var editor=await SecurityAdministrationPlanATests.MakeAdmin(app,await app.AddAccountAsync(store,"*"));
        var limited=await app.AddAccountAsync(store,PermissionCodes.Pos.Order.View);
        var hidden=await app.AddAccountAsync(store,"*");
        var foreign=await app.AddAccountAsync(app.Stores[1],"*");
        int homeId,ordersId,receivingId,salesId,customId;
        await using(var db=app.Database.CreateTenantContext(store.StoreId)){
            // Fixture-owned menu records only. The application never creates these defaults in store data.
            var pos=new AdminMenuItem{StoreId=store.StoreId,Title="Bán hàng của cửa hàng",IsActive=true,SortOrder=10};
            var warehouse=new AdminMenuItem{StoreId=store.StoreId,Title="Kho của cửa hàng",IsActive=true,SortOrder=20};
            db.AddRange(pos,warehouse);await db.SaveChangesAsync();
            AdminMenuItem Link(string title,string url,string permission,int order,int? parent,string icon)=>new(){StoreId=store.StoreId,Title=title,Url=url,PermissionCode=permission,SortOrder=order,ParentId=parent,Icon=icon,IsActive=true};
            var home=Link("Trang tổng quan đã cấu hình","/admin/home",PermissionCodes.Admin.DashboardView,0,null,"bx bx-home-alt");
            var orders=Link("Danh sách hóa đơn của tiệm","/admin/pos/orders-page",PermissionCodes.Pos.Order.View,20,pos.Id,"bx bx-receipt");
            var receiving=Link("Nhập hàng nhanh tại kho","/admin/warehouse-receiving",AppPermissions.InventoryStockDocumentCreate,10,warehouse.Id,"bx bx-package");
            var sales=Link("Bán tại quầy của tiệm","/admin/pos",PermissionCodes.Pos.Order.View,10,pos.Id,"bx bx-shopping-bag");
            var custom=Link("In tem riêng của tiệm","/admin/label-printing",PermissionCodes.System.ProductLabel.Print,30,warehouse.Id,"bx bx-barcode");
            db.AddRange(home,orders,receiving,sales,custom);await db.SaveChangesAsync();
            db.Add(new AdminMenuVisibilitySetting{StoreId=store.StoreId,RoleId=hidden.RoleId,HiddenMenuIdsJson=JsonSerializer.Serialize(new[]{pos.Id})});
            await db.SaveChangesAsync();homeId=home.Id;ordersId=orders.Id;receivingId=receiving.Id;salesId=sales.Id;customId=custom.Id;
        }
        var root=FullApplicationFixture.SourceRoot();var output=MobileAcceptanceEvidence.PathFor(root,"TestResults","mobile-primary-nav-20261028");Directory.CreateDirectory(output);
        object Account(FullApplicationFixture.Account a)=>new{user=a.Name,password=a.Password,baseUrl=$"http://{a.Store.Host}:{app.Address.Port}",terminalId=a.Store.TerminalId};
        var before=await Protected(app);
        await Node("defaults",new{full=Account(full),editor=Account(editor),limited=Account(limited),hidden=Account(hidden),foreign=Account(foreign)});
        Assert.Equal(before,await Protected(app));
        await using(var db=app.Database.CreateTenantContext(store.StoreId)){
            Assert.Empty(await db.Set<AdminMenuPinSetting>().Where(x=>x.StoreId==store.StoreId).ToArrayAsync());
            db.Add(new AdminMenuPinSetting{StoreId=store.StoreId,PinnedMenuIdsJson=JsonSerializer.Serialize(new[]{customId})});await db.SaveChangesAsync();
        }
        var customBefore=await Protected(app);await Node("custom",new{full=Account(full)});Assert.Equal(customBefore,await Protected(app));
        await using(var db=app.Database.CreateHostContext()){Assert.False(db.Database.HasPendingModelChanges());Assert.Empty(await db.Database.GetPendingMigrationsAsync());}
        await File.WriteAllTextAsync(Path.Combine(output,"sql-checks.json"),JsonSerializer.Serialize(new{passed=true,strictMigrations=true,configurationUnchangedByRendering=true,defaultNavigationCreatesNoPinSettings=true,customPinsPreserved=true,existingChromeWindowsPreserved=true}));
        async Task Node(string mode,object accounts){
            var start=new ProcessStartInfo("node"){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
            start.Environment["NODE_PATH"]=Path.Combine(root,"Logs","pos-offline-browser-deps","node_modules");start.ArgumentList.Add(Path.Combine(root,"GaoApp.Tests.Browser","mobile-primary-navigation.browser.cjs"));
            using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new{mode,accounts,output,homeId,ordersId,receivingId,salesId,customId,limitedRoleId=limited.RoleId}));process.StandardInput.Close();
            using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(8));try{await process.WaitForExitAsync(timeout.Token);}catch{if(!process.HasExited)process.Kill(true);throw;}
            var log=await stdout+"\n"+await stderr;await File.WriteAllTextAsync(Path.Combine(output,mode+".log"),log);Assert.True(process.ExitCode==0,log);
        }
    }

    private static async Task<string> Protected(FullApplicationFixture app)
    {
        await using var db=app.Database.CreateHostContext();
        async Task<object> Rows<T>()where T:BaseEntity{var rows=await db.Set<T>().IgnoreQueryFilters().AsNoTracking().OrderBy(x=>x.Id).ToArrayAsync();return rows.Select(row=>db.Entry(row).Properties.OrderBy(p=>p.Metadata.Name).ToDictionary(p=>p.Metadata.Name,p=>p.CurrentValue)).ToArray();}
        var json=JsonSerializer.Serialize(new{menus=await Rows<AdminMenuItem>(),pins=await Rows<AdminMenuPinSetting>(),visibility=await Rows<AdminMenuVisibilitySetting>(),stores=await Rows<Store>(),roles=await Rows<Role>(),grants=await db.RolePermissions.OrderBy(x=>x.Id).Select(x=>new{x.Id,x.RoleId,x.PermissionId}).ToArrayAsync()});
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)));
    }
}
