using System.Net;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;
namespace GaoApp.Tests.Security;
[Collection("SqlServerConcurrency")]
public sealed class CustomerProfileSqlServerTests
{
    internal sealed record Seed(int CustomerId,int OrderId,int VoucherId);
    internal static async Task<Seed> SeedAsync(FullApplicationFixture app,FullApplicationFixture.StoreSeed store,int userId)
    {
        await using var db=app.Database.CreateTenantContext(store.StoreId);
        var customer=new Customer {StoreId=store.StoreId,Name="Nguyễn Minh An",Code="KH-PROFILE",Phone="0901234567",IsImportedFromOldSystem=true,Note="Khách mua thường xuyên"};
        var settings=await db.RewardSettings.FirstOrDefaultAsync();
        if(settings==null){settings=new RewardSettings {StoreId=store.StoreId};db.Add(settings);}
        settings.MoneyPerPoint=1000;settings.PointsPerVoucher=30;settings.VoucherValue=30000;settings.IsEnabled=true;
        var shift=new POSShift {StoreId=store.StoreId,TerminalId=store.TerminalId,WarehouseId=store.WarehouseId,OpenedByUserId=userId};
        var date=new DateTime(2026,9,2,5,0,0,DateTimeKind.Utc);
        var order=new Order {StoreId=store.StoreId,Customer=customer,POSShift=shift,OrderNumber="PROFILE-SALE",Status=OrderStatus.Completed,PaymentStatus=PaymentStatus.Paid,GrandTotal=100000,PaidTotal=100000,CompletedAtUtc=date};
        var refund=new Order {StoreId=store.StoreId,Customer=customer,POSShift=shift,OrderNumber="PROFILE-REFUND",Status=OrderStatus.Refunded,GrandTotal=50000,CompletedAtUtc=date};
        db.AddRange(order,refund,new Order {StoreId=store.StoreId,Customer=customer,POSShift=shift,OrderNumber="PROFILE-VOID",Status=OrderStatus.Voided,GrandTotal=999999});
        db.Add(new OrderPayment {StoreId=store.StoreId,Order=order,Amount=100000,Method=PaymentMethod.BankTransfer});
        db.AddRange(new SalesReturn {StoreId=store.StoreId,Order=order,POSShift=shift,ReturnNumber="PROFILE-PART",Reason="Trả một phần",Status=SalesReturnStatus.Completed,RefundTotal=10000,CreatedByUserId=userId},
            new SalesReturn {StoreId=store.StoreId,Order=refund,POSShift=shift,ReturnNumber="PROFILE-FULL",Reason="Hoàn toàn bộ",Status=SalesReturnStatus.Completed,RefundTotal=50000,CreatedByUserId=userId});
        var voucher=new CustomerRewardVoucher {StoreId=store.StoreId,Customer=customer,VoucherCode="VC-PROFILE-USED",Value=30000,RequiredAmount=3000,Status=CustomerRewardVoucherStatus.Used,IssuedAtUtc=date.AddDays(1),UsedAtUtc=date.AddDays(2),UsedOrder=order};
        db.AddRange(voucher,new CustomerRewardVoucher {StoreId=store.StoreId,Customer=customer,VoucherCode="VC-PROFILE-READY",Value=10000,RequiredAmount=1000,IssuedAtUtc=date.AddDays(2)});
        var entries=new List<CustomerRewardLedger> {
            new() {StoreId=store.StoreId,Customer=customer,Type=CustomerRewardLedgerType.ImportOldBalance,Amount=10000,Description="Số dư cũ"},
            new() {StoreId=store.StoreId,Customer=customer,Order=order,Type=CustomerRewardLedgerType.SaleEarned,Amount=2500,Description="Mua hàng"},
            new() {StoreId=store.StoreId,Customer=customer,Voucher=voucher,Type=CustomerRewardLedgerType.VoucherRedeemed,Amount=-3000,Description="Đổi voucher"}
        };
        for(var i=0;i<20;i++)entries.Add(new() {StoreId=store.StoreId,Customer=customer,Type=CustomerRewardLedgerType.ManualAdjust,Amount=100,Description="Điều chỉnh kiểm thử"});
        db.AddRange(entries);await db.SaveChangesAsync();
        for(var i=0;i<entries.Count;i++)await db.CustomerRewardLedgers.Where(x=>x.Id==entries[i].Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CreatedAtUtc,date.AddDays(i<3?i-1:2).AddSeconds(i)).SetProperty(x=>x.CreatedBy,(int?)userId));
        return new(customer.Id,order.Id,voucher.Id);
    }
    [Fact]
    public async Task Profile_uses_reward_balance_full_ledger_running_balances_filters_and_permissions()
    {
        await using var app=await FullApplicationFixture.StartAsync();var store=app.Stores[0];
        var account=await app.AddAccountAsync(store,"*");var seed=await SeedAsync(app,store,account.UserId);
        using var admin=await app.LoginAsync(account);using var viewer=await app.LoginAsync(await app.AddAccountAsync(store,PermissionCodes.Catalog.Customer.View));
        using var denied=await app.LoginAsync(await app.AddAccountAsync(store,PermissionCodes.Pos.Order.View));
        using var foreign=await app.LoginAsync(await app.AddAccountAsync(app.Stores[1],"*"));
        var url=$"/admin/customers/{seed.CustomerId}/profile";
        var html=await admin.Http.GetStringAsync(url);Assert.Contains("Nguyễn Minh An",System.Net.WebUtility.HtmlDecode(html));Assert.Contains("90.000",html);Assert.Contains("cpPoints\">11<",html);
        using(var response=await admin.Http.GetAsync(url+"/data")) {
            if(!response.IsSuccessStatusCode) {
                var output=(System.Collections.Concurrent.ConcurrentQueue<string>)typeof(FullApplicationFixture).GetField("output",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(app)!;
                Assert.Fail(string.Join("\n",output.TakeLast(90)));
            }
        }
        var overview=await admin.JsonAsync(HttpMethod.Get,url+"/data");Assert.Equal(20,overview.GetProperty("items").GetArrayLength());
        var page=await admin.JsonAsync(HttpMethod.Get,url+"/data?tab=points&page=2");Assert.Equal(23,page.GetProperty("total").GetInt32());Assert.Equal(3,page.GetProperty("items").GetArrayLength());
        var filtered=await admin.JsonAsync(HttpMethod.Get,url+"/data?tab=points&status=4&from=2026-09-03&to=2026-09-03");
        var entry=Assert.Single(filtered.GetProperty("items").EnumerateArray());Assert.Equal(9500,entry.GetProperty("balanceAmount").GetDecimal());Assert.Equal(9,entry.GetProperty("balancePoints").GetDecimal());Assert.Equal(-3,entry.GetProperty("deltaPoints").GetDecimal());
        var order=Assert.Single((await admin.JsonAsync(HttpMethod.Get,url+"/data?tab=orders&search=PROFILE-SALE")).GetProperty("items").EnumerateArray());Assert.Contains(order.GetProperty("paymentMethods").EnumerateArray(),x=>x.GetInt32()==1);
        Assert.Single((await admin.JsonAsync(HttpMethod.Get,url+"/data?tab=vouchers&status=1")).GetProperty("items").EnumerateArray());
        using(var response=await viewer.Http.GetAsync(url+"/data?tab=orders"))Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        using(var response=await denied.Http.GetAsync(url))Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        using(var response=await foreign.Http.GetAsync(url+"/data?tab=points"))Assert.Equal(HttpStatusCode.NotFound,response.StatusCode);
        var restricted=await viewer.JsonAsync(HttpMethod.Get,url+"/data");Assert.DoesNotContain(restricted.GetProperty("items").EnumerateArray(),x=>x.GetProperty("kind").GetString()=="order"||x.GetProperty("orderId").ValueKind!=JsonValueKind.Null);
        using(var response=await admin.Http.GetAsync(url+"/data?tab=points&from=2026-09-10&to=2026-09-01"))Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        await using(var db=app.Database.CreateTenantContext(store.StoreId)){Assert.Equal(11500,await db.CustomerRewardLedgers.SumAsync(x=>x.Amount));Assert.Equal(23,await db.CustomerRewardLedgers.CountAsync());await db.RewardSettings.ExecuteDeleteAsync();}
        Assert.Contains("cpPoints\">—<",System.Net.WebUtility.HtmlDecode(await admin.Http.GetStringAsync(url)));
        Assert.Null((await admin.JsonAsync(HttpMethod.Get,url+"/data?tab=points")).GetProperty("moneyPerPoint").GetString());
    }
}
