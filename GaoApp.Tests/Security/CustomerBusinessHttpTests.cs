using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Web.Services.CustomerService;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GaoApp.Tests.Security;
[Collection("SqlServerConcurrency")]
public sealed class CustomerBusinessHttpTests
{
    private static string Field(string html,string name)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\""+name+"\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
    private static HttpClient Guest(FullApplicationFixture app,string host){var h=new HttpClient(new HttpClientHandler{CookieContainer=new(),AllowAutoRedirect=false}){BaseAddress=app.Address,Timeout=TimeSpan.FromSeconds(30)};h.DefaultRequestHeaders.Host=host;return h;}
    private static async Task<JsonElement> Post(HttpClient client,string url,object payload){using var r=await client.PostAsJsonAsync(url,payload);var raw=await r.Content.ReadAsStringAsync();Assert.True(r.IsSuccessStatusCode,$"{url}: {(int)r.StatusCode} {raw}");return JsonDocument.Parse(raw).RootElement.Clone();}
    [Fact]
    public async Task Real_HTTP_verifies_scoped_customer_reads_live_orders_rewards_and_revokes_on_rights_changes()
    {
        await using var app=await FullApplicationFixture.StartAsync();var shop=app.Stores[0];
        var rights=new[]{CustomerServiceRights.View,CustomerServiceRights.Reply,CustomerServiceRights.Manage,CustomerServiceRights.Verify,PermissionCodes.Catalog.Customer.View,PermissionCodes.Pos.Order.View};
        var agent=await app.AddAccountAsync(shop,rights);var reader=await app.AddAccountAsync(shop,CustomerServiceRights.View);var limited=await app.AddAccountAsync(shop,CustomerServiceRights.View,CustomerServiceRights.Reply,CustomerServiceRights.Verify);var foreign=await app.AddAccountAsync(app.Stores[1],rights);
        int ownCustomer,otherCustomer,foreignCustomer;int orderId;
        await using(var db=app.Database.CreateTenantContext(shop.StoreId)){
            var customer=new Customer{StoreId=shop.StoreId,Name="TEST owner",Code="KH-CSKH-D",Phone="0901234567",Note="PRIVATE_NOTE",Address="PRIVATE_ADDRESS",TaxCode="PRIVATE_TAX"};
            var other=new Customer{StoreId=shop.StoreId,Name="TEST other",Code="KH-OTHER-D",Phone="0901234567"};db.AddRange(customer,other);
            var shift=new POSShift{StoreId=shop.StoreId,TerminalId=shop.TerminalId,WarehouseId=shop.WarehouseId,OpenedByUserId=agent.UserId};
            var sale=new Order{StoreId=shop.StoreId,Customer=customer,POSShift=shift,OrderNumber="CSKH-OWN-D",CompletedAtUtc=DateTime.UtcNow.AddDays(-1),Status=OrderStatus.Completed,PaymentStatus=PaymentStatus.Paid,GrandTotal=18000,PaidTotal=18000,Note="PRIVATE_ORDER_NOTE"};
            var variant=await db.ProductVariants.SingleAsync(v=>v.Id==shop.VariantId);sale.Lines.Add(new OrderLine{StoreId=shop.StoreId,ProductId=variant.ProductId,VariantId=variant.Id,ItemName="TEST Milk <img src=x onerror=alert(1)>",SellingUnitName="Hộp",Quantity=2,UnitPrice=9000,LineTotal=18000,UnitCostSnapshot=3333,LineCostTotal=6666,GrossProfit=11334});
            db.AddRange(sale,new Order{StoreId=shop.StoreId,Customer=other,POSShift=shift,OrderNumber="CSKH-OTHER-D",CompletedAtUtc=DateTime.UtcNow,Status=OrderStatus.Completed,GrandTotal=999999},new Order{StoreId=shop.StoreId,Customer=customer,POSShift=shift,OrderNumber="CSKH-OLD-D",CompletedAtUtc=DateTime.UtcNow.AddDays(-91),Status=OrderStatus.Completed},new Order{StoreId=shop.StoreId,Customer=customer,POSShift=shift,OrderNumber="CSKH-DRAFT-D",Status=OrderStatus.Draft});
            var settings=await db.RewardSettings.FirstOrDefaultAsync();if(settings is null){settings=new RewardSettings{StoreId=shop.StoreId};db.Add(settings);}settings.IsEnabled=true;settings.MoneyPerPoint=65000;settings.PointsPerVoucher=5;settings.VoucherValue=30000;
            db.Add(new CustomerRewardLedger{StoreId=shop.StoreId,Customer=customer,Type=CustomerRewardLedgerType.ManualAdjust,Amount=650000,Description="TEST reward"});await db.SaveChangesAsync();ownCustomer=customer.Id;otherCustomer=other.Id;orderId=sale.Id;
        }
        await using(var db=app.Database.CreateTenantContext(app.Stores[1].StoreId)){var foreignRecord=new Customer{StoreId=app.Stores[1].StoreId,Name="FOREIGN_TEST_CUSTOMER",Code="KH-CSKH-D"};db.Add(foreignRecord);await db.SaveChangesAsync();foreignCustomer=foreignRecord.Id;}
        using var staff=await app.LoginAsync(agent);using var read=await app.LoginAsync(reader);using var noBusiness=await app.LoginAsync(limited);using var wrongStore=await app.LoginAsync(foreign);
        var inbox=await staff.Http.GetStringAsync("/admin/customer-service");using(var enable=await staff.Http.PostAsync("/admin/customer-service/settings",new FormUrlEncodedContent(new Dictionary<string,string>{["enabled"]="true",["version"]=Field(inbox,"version")})))Assert.Equal(HttpStatusCode.Redirect,enable.StatusCode);
        using var guest=Guest(app,shop.Host);var html=await guest.GetStringAsync("/support");guest.DefaultRequestHeaders.Add("RequestVerificationToken",Field(html,"__RequestVerificationToken"));
        var c=await Post(guest,"/support/conversations",new{requestId=Guid.NewGuid(),name="SELF DECLARED TEST",contact="0901234567",consent=true});var id=c.GetProperty("id").GetGuid();var publicBase=$"/support/conversations/{id}/private";var adminBase=$"/admin/customer-service/conversations/{id}/identity";
        using(var before=await guest.GetAsync(publicBase+"/orders?customerId="+ownCustomer))Assert.Equal(HttpStatusCode.Forbidden,before.StatusCode);
        using(var missingCsrf=Guest(app,shop.Host)){await missingCsrf.GetStringAsync("/support");using var denied=await missingCsrf.PostAsJsonAsync(publicBase+"/verify",new{requestId=Guid.NewGuid(),code="X"});Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);}
        foreach(var deniedClient in new[]{read.Http,noBusiness.Http})using(var denied=await deniedClient.PostAsJsonAsync(adminBase+"/customers",new{query="KH-CSKH-D"}))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var wrong=await wrongStore.Http.PostAsJsonAsync(adminBase+"/customers",new{query="KH-CSKH-D"}))Assert.Equal(HttpStatusCode.NotFound,wrong.StatusCode);
        var rows=await Post(staff.Http,adminBase+"/customers",new{query="KH-CSKH-D"});Assert.Equal(ownCustomer,Assert.Single(rows.EnumerateArray()).GetProperty("id").GetInt32());Assert.DoesNotContain("0901234567",rows.GetRawText());Assert.DoesNotContain("FOREIGN",rows.GetRawText());
        var phoneRows=await Post(staff.Http,adminBase+"/customers",new{query="0901234567"});Assert.Equal(2,phoneRows.GetArrayLength());
        c=await Post(staff.Http,$"/admin/customer-service/conversations/{id}/commands",new{requestId=Guid.NewGuid(),action="claim",version=c.GetProperty("version").GetInt64()});
        var issue=new{requestId=Guid.NewGuid(),version=c.GetProperty("version").GetInt64(),customerId=ownCustomer,method="in_person",reference="TEST counter identity checked",confirmed=true};
        using(var cross=await staff.Http.PostAsJsonAsync(adminBase+"/issue",new{requestId=Guid.NewGuid(),version=issue.version,customerId=foreignCustomer,method="in_person",reference="TEST identity",confirmed=true}))Assert.Equal(HttpStatusCode.NotFound,cross.StatusCode);
        var issued=await Post(staff.Http,adminBase+"/issue",issue);var code=issued.GetProperty("code").GetString()!;Assert.Equal(24,code.Length);Assert.Equal(code,(await Post(staff.Http,adminBase+"/issue",issue)).GetProperty("code").GetString());
        using var outsider=Guest(app,shop.Host);await outsider.GetStringAsync("/support");using(var denied=await outsider.GetAsync(publicBase+"/status"))Assert.Equal(HttpStatusCode.NotFound,denied.StatusCode);
        var verify=new{requestId=Guid.NewGuid(),code};Assert.True((await Post(guest,publicBase+"/verify",verify)).GetProperty("verified").GetBoolean());Assert.True((await Post(guest,publicBase+"/verify",verify)).GetProperty("verified").GetBoolean());
        var orders=await guest.GetFromJsonAsync<JsonElement>(publicBase+"/orders?customerId="+otherCustomer);var list=orders.GetProperty("items");Assert.Equal("CSKH-OWN-D",Assert.Single(list.EnumerateArray()).GetProperty("number").GetString());Assert.DoesNotContain("PRIVATE",orders.GetRawText());Assert.DoesNotContain("customerId",orders.GetRawText());
        var detail=await Post(guest,publicBase+"/order",new{number="CSKH-OWN-D"});Assert.Equal(18000,detail.GetProperty("total").GetDecimal());Assert.Equal(9000,detail.GetProperty("lines")[0].GetProperty("unitPrice").GetDecimal());foreach(var key in new[]{"unitCost","grossProfit","costSnapshot","PRIVATE_","customerId"})Assert.DoesNotContain(key,detail.GetRawText(),StringComparison.OrdinalIgnoreCase);
        foreach(var number in new[]{"CSKH-OTHER-D","CSKH-DRAFT-D","CSKH-OLD-D"})using(var denied=await guest.PostAsJsonAsync(publicBase+"/order",new{number}))Assert.Equal(HttpStatusCode.NotFound,denied.StatusCode);
        var balance=await guest.GetFromJsonAsync<JsonElement>(publicBase+"/rewards");Assert.Equal(650000,balance.GetProperty("reward").GetProperty("balanceAmount").GetDecimal());Assert.Equal(10,balance.GetProperty("reward").GetProperty("points").GetDecimal());Assert.Equal(2,balance.GetProperty("reward").GetProperty("redeemableVoucherCount").GetInt32());
        await using(var db=app.Database.CreateTenantContext(shop.StoreId)){await db.Orders.Where(o=>o.Id==orderId).ExecuteUpdateAsync(s=>s.SetProperty(o=>o.GrandTotal,19000m));}
        Assert.Equal(19000,(await Post(guest,publicBase+"/order",new{number="CSKH-OWN-D"})).GetProperty("total").GetDecimal());
        var transcript=await guest.GetStringAsync($"/support/conversations/{id}");Assert.DoesNotContain(code,transcript);Assert.DoesNotContain("CSKH-OWN-D",transcript);Assert.DoesNotContain("650000",transcript);
        // The real cookie and grant survive an IIS-like worker restart; source permissions are still read afresh.
        await app.RestartAsync(enableDeliveryOutbox:false);Assert.True((await guest.GetFromJsonAsync<JsonElement>(publicBase+"/status")).GetProperty("verified").GetBoolean());
        await using(var db=app.Database.CreateHostContext()){var membership=await db.UserInStores.IgnoreQueryFilters().SingleAsync(m=>m.UserId==agent.UserId&&m.StoreId==shop.StoreId);membership.IsActive=false;await db.SaveChangesAsync();}
        using(var denied=await guest.GetAsync(publicBase+"/orders"))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.False((await guest.GetFromJsonAsync<JsonElement>(publicBase+"/status")).GetProperty("verified").GetBoolean());
        await using(var db=app.Database.CreateHostContext()){var membership=await db.UserInStores.IgnoreQueryFilters().SingleAsync(m=>m.UserId==agent.UserId&&m.StoreId==shop.StoreId);membership.IsActive=true;await db.SaveChangesAsync();}
        Assert.True((await guest.GetFromJsonAsync<JsonElement>(publicBase+"/status")).GetProperty("verified").GetBoolean());
        await Post(guest,publicBase+"/revoke",new{requestId=Guid.NewGuid()});using(var revoked=await guest.GetAsync(publicBase+"/rewards"))Assert.Equal(HttpStatusCode.Forbidden,revoked.StatusCode);
        using(var reused=await guest.PostAsJsonAsync(publicBase+"/verify",new{requestId=Guid.NewGuid(),code}))Assert.Equal(HttpStatusCode.Forbidden,reused.StatusCode);
        var output=Path.Combine(FullApplicationFixture.SourceRoot(),"Logs","customer-service-step-d");Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,"support-page.html"),html);await File.WriteAllTextAsync(Path.Combine(output,"inbox-page.html"),inbox);
    }
}