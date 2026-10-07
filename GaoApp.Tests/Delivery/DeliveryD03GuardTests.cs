using System.Net;
using System.Net.Http.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static GaoApp.Tests.Delivery.DeliveryD03Support;

namespace GaoApp.Tests.Delivery;

[Collection("DeliveryD03"), Trait("Category","DeliveryD03")]
public sealed class DeliveryD03GuardTests(DeliveryD02Fixture fixture)
{
    [Theory]
    [InlineData("version")] [InlineData("fingerprint")] [InlineData("line")]
    public async Task Stale_server_snapshot_cannot_transfer(string mutation)
    {
        using var c=await fixture.CaseAsync(false); var body=await Request(c);
        if(mutation=="version") body=body with {ExpectedVersion=Convert.ToBase64String(new byte[8])};
        if(mutation=="fingerprint") body=body with {ExpectedFingerprint=new string('A',64)};
        if(mutation=="line") await fixture.Web.Database.ExecuteAsync($"UPDATE OrderLines SET ItemName=N'Changed independently' WHERE OrderId={c.CartId}");
        await Reject(c,body,HttpStatusCode.Conflict,"CART_CHANGED");
    }
    [Theory]
    [InlineData("payment","PAYMENT_EXISTS")] [InlineData("deposit","PAYMENT_EXISTS")]
    [InlineData("promotion","PRICE_FEATURE_UNSUPPORTED")] [InlineData("multiAllocation","SOURCE_ALREADY_ALLOCATED")]
    public async Task Payment_deposit_and_unsupported_price_or_allocations_do_not_transfer(string kind,string code)
    {
        using var c=await fixture.CaseAsync(false); var body=await Request(c); await using var db=c.Context();
        var cart=await db.Orders.SingleAsync(x=>x.Id==c.CartId);
        if(kind=="payment") db.OrderPayments.Add(new(){StoreId=c.Account.Store.StoreId,OrderId=cart.Id,Amount=1,Method=PaymentMethod.Cash});
        if(kind=="deposit") cart.DepositAmount=1;
        if(kind=="promotion") cart.PromotionDiscountTotal=1;
        if(kind=="multiAllocation") cart.HasMultipleLegalEntities=true;
        await db.SaveChangesAsync(); await Reject(c,body,HttpStatusCode.Conflict,code);
    }
    [Theory]
    [InlineData(PosPaymentQrStatus.Pending)] [InlineData(PosPaymentQrStatus.Expired)]
    public async Task Bank_QR_requires_cancellation_even_if_expired(PosPaymentQrStatus status)
    {
        using var c=await fixture.CaseAsync(false); var body=await Request(c); await using var db=c.Context();
        var bank=new StoreBankAccount{StoreId=c.Account.Store.StoreId,BankCode="ACB",BankName="D03 test",AccountNumber=Guid.NewGuid().ToString("N"),AccountName="Test",IsActive=true};
        db.Add(bank); await db.SaveChangesAsync();
        db.Add(new PosPaymentQrRequest{StoreId=c.Account.Store.StoreId,OrderId=c.CartId,BankAccountId=bank.Id,Amount=40,Status=status,RequestCode="D03-"+Guid.NewGuid().ToString("N"),ExpireAtUtc=DateTime.UtcNow.AddMinutes(-1)});
        await db.SaveChangesAsync(); await Reject(c,body,HttpStatusCode.Conflict,"BANK_QR_EXISTS");
    }
    [Fact]
    public async Task Cross_store_source_cart_is_never_loaded()
    {
        using var c=await fixture.CaseAsync(false); var body=await Request(c);
        using var other=await fixture.Web.LoginAsync(await fixture.Web.AddAccountAsync(fixture.Web.Stores[1],"*"));
        await other.JsonAsync(HttpMethod.Post,"/admin/pos/shift/open",new{openingCash=0,warehouseId=fixture.Web.Stores[1].WarehouseId});
        using var r=await other.Http.PostAsJsonAsync(CreatePath,body); Assert.Equal(HttpStatusCode.NotFound,r.StatusCode);
        Assert.Contains("SOURCE_NOT_FOUND",await r.Content.ReadAsStringAsync());
    }
    [Fact]
    public async Task Freshly_revoked_create_permission_blocks_snapshot_and_transfer()
    {
        using var c=await fixture.CaseAsync(false); var body=await Request(c);
        await fixture.Web.Database.ExecuteAsync($"DELETE rp FROM RolePermissions rp JOIN Permissions p ON rp.PermissionId=p.Id WHERE rp.RoleId={c.Account.RoleId} AND p.Code='{PermissionCodes.Delivery.Create}'");
        using var denied=await c.Client.Http.PostAsJsonAsync(CreatePath,body); Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using var snapshot=await c.Client.Http.GetAsync("/admin/api/deliveries/current-cart");Assert.Equal(HttpStatusCode.Forbidden,snapshot.StatusCode);
        await using var db=c.Context(); Assert.False(await db.DeliveryOrders.AnyAsync(x=>x.SourceCartId==c.CartId));
    }
    [Fact]
    public async Task Inactive_source_warehouse_is_rejected()
    {
        using var c=await fixture.CaseAsync(false); var body=await Request(c);
        await fixture.Web.Database.ExecuteAsync($"UPDATE Warehouses SET IsActive=0 WHERE Id={c.Account.Store.WarehouseId}");
        try { await Reject(c,body,HttpStatusCode.Forbidden,"SOURCE_SCOPE_INVALID"); }
        finally { await fixture.Web.Database.ExecuteAsync($"UPDATE Warehouses SET IsActive=1 WHERE Id={c.Account.Store.WarehouseId}"); }
    }
    [Fact]
    public async Task Offline_header_is_explicitly_rejected()
    {
        using var c=await fixture.CaseAsync(false); var body=await Request(c); c.Client.Http.DefaultRequestHeaders.Add("X-POS-Offline","1");
        await Reject(c,body,HttpStatusCode.Conflict,"ONLINE_REQUIRED");
    }
    [Fact]
    public async Task Client_cannot_supply_actor_store_source_warehouse_or_amount()
    {
        using var c=await fixture.CaseAsync(false); var body=await Request(c);
        using var r=await c.Client.Http.PostAsJsonAsync(CreatePath,new{body.ClientRequestId,body.SourceCartId,body.ExpectedVersion,body.ExpectedFingerprint,body.RecipientName,body.RecipientPhone,body.RecipientAddress,body.Note,storeId=999,sourceWarehouseId=999,actorId=999,amount=1});
        Assert.Equal(HttpStatusCode.BadRequest,r.StatusCode); await using var db=c.Context(); Assert.False(await db.DeliveryOrders.AnyAsync(x=>x.SourceCartId==c.CartId));
    }
    [Fact]
    public async Task Post_requires_antiforgery_token()
    {
        using var c=await fixture.CaseAsync(false); var body=await Request(c); c.Client.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using var r=await c.Client.Http.PostAsJsonAsync(CreatePath,body); Assert.Equal(HttpStatusCode.BadRequest,r.StatusCode);
    }
    [Fact]
    public async Task Replay_key_cannot_be_used_by_another_employee()
    {
        using var c=await fixture.CaseAsync(false);var body=await Request(c);await Create(c,body);
        using var other=await fixture.Web.LoginAsync(await fixture.Web.AddAccountAsync(c.Account.Store,"*"));
        using var r=await other.Http.PostAsJsonAsync(CreatePath,body);Assert.Equal(HttpStatusCode.Conflict,r.StatusCode);Assert.Contains("REQUEST_KEY_CONFLICT",await r.Content.ReadAsStringAsync());
        await using var db=c.Context();Assert.Equal(1,await db.DeliveryOrders.CountAsync(x=>x.SourceCartId==c.CartId));
    }
    [Fact]
    public async Task Replay_key_cannot_be_used_at_another_terminal_by_same_employee()
    {
        using var c=await fixture.CaseAsync(false);var body=await Request(c);await Create(c,body);
        var account=c.Account with {Store=c.Account.Store with {TerminalId=fixture.Web.Stores[0].TerminalId}};
        using var other=await fixture.Web.LoginAsync(account);using var r=await other.Http.PostAsJsonAsync(CreatePath,body);
        Assert.Equal(HttpStatusCode.Conflict,r.StatusCode);Assert.Contains("REQUEST_KEY_CONFLICT",await r.Content.ReadAsStringAsync());
    }
    [Fact]
    public async Task Inactive_terminal_cannot_transfer()
    {
        using var c=await fixture.CaseAsync(false);var body=await Request(c);
        await fixture.Web.Database.ExecuteAsync($"UPDATE POSTerminals SET IsActive=0 WHERE Id={c.Account.Store.TerminalId}");
        await Reject(c,body,HttpStatusCode.Forbidden,"DELIVERY_FORBIDDEN");
    }
    [Fact]
    public async Task Changing_current_cart_blocks_transfer_of_the_previous_cart()
    {
        using var c=await fixture.CaseAsync(false);var body=await Request(c);
        await using (var db=c.Context()) {
            var changed=await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE POSShifts SET CurrentOrderId=NULL WHERE TerminalId={c.Account.Store.TerminalId} AND Status={(int)POSShiftStatus.Open}");
            Assert.Equal(1,changed);
        }
        await Reject(c,body,HttpStatusCode.Conflict,"NOT_CURRENT_CART");
    }
    [Theory]
    [InlineData(AcbSessionStatus.Creating)] [InlineData(AcbSessionStatus.Received)]
    public async Task Unresolved_ACB_session_blocks_transfer_even_with_cancelled_manual_QR(AcbSessionStatus status)
    {
        using var c=await fixture.CaseAsync(false);var body=await Request(c);await using var db=c.Context();
        var bank=new StoreBankAccount{StoreId=c.Account.Store.StoreId,BankCode="ACB",BankName="Test",AccountNumber=Guid.NewGuid().ToString("N"),AccountName="Test",IsActive=true};db.Add(bank);await db.SaveChangesAsync();
        var qr=new PosPaymentQrRequest{StoreId=c.Account.Store.StoreId,OrderId=c.CartId,BankAccountId=bank.Id,Amount=40,Status=PosPaymentQrStatus.Cancelled,RequestCode="D03-"+Guid.NewGuid().ToString("N"),ExpireAtUtc=DateTime.UtcNow.AddMinutes(5)};db.Add(qr);await db.SaveChangesAsync();
        var shift=await db.POSShifts.SingleAsync(x=>x.TerminalId==c.Account.Store.TerminalId && x.Status==POSShiftStatus.Open);
        db.Add(new AcbQrSession{StoreId=c.Account.Store.StoreId,QrRequestId=qr.Id,OrderId=c.CartId,ShiftId=shift.Id,TerminalId=shift.TerminalId,CashierId=c.Account.UserId,Amount=40,Status=status,ProviderOrderId=Guid.NewGuid().ToString("N"),CartFingerprint=body.ExpectedFingerprint});await db.SaveChangesAsync();
        await Reject(c,body,HttpStatusCode.Conflict,"BANK_QR_EXISTS");
    }
}
