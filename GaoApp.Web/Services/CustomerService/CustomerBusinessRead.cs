using GaoApp.Application.Interfaces.Services.Rewards;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Services.Assistant;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.CustomerService;

// Personal business data stays in GaoApp. It is never inserted into the shared chat transcript or an LLM prompt.
public sealed class CustomerBusinessRead(AppDbContext db,CustomerServiceStore storage,CustomerServiceAccess access,
    ICustomerRewardService rewards,IDataProtectionProvider protection,TimeProvider clock)
{
    public static bool PrivateQuery(string message)=>System.Text.RegularExpressions.Regex.IsMatch(AssistantCatalogSearch.Canonical(message),@"\b(?:don hang cua (?:toi|minh|em)|kiem tra don|tra cuu (?:don|diem|voucher|phieu)|lich su mua|don mua|diem cua (?:toi|minh|em)|(?:toi|minh|em) (?:co |con )?bao nhieu diem|tich luy cua (?:toi|minh|em)|voucher cua (?:toi|minh|em)|phieu (?:uu dai )?cua (?:toi|minh|em)|ma voucher cua (?:toi|minh|em))\b");
    public static bool VerificationCodeCandidate(string message)=>System.Text.RegularExpressions.Regex.IsMatch(message.Trim(),@"\A(?:[A-Fa-f0-9]{24}|(?:[A-Fa-f0-9]{4}[- ]?){5}[A-Fa-f0-9]{4})\z");
    public const string PrivateReply="Mình mở mục Đơn mua & quyền lợi ngay trên khung chat để xem riêng. Nếu chưa xác minh, chọn Gặp nhân viên để được cấp mã. Đơn và số điểm được đọc trực tiếp từ cửa hàng, không gửi trong tin nhắn.";
    private IDataProtector Codes=>protection.CreateProtector("GaoApp.CustomerService.IdentityCode.v1");
    private static AssistantException Required()=>new(403,"customer_verification_required","Mình cần được nhân viên xác minh trước khi tra cứu. Chọn Gặp nhân viên để được hỗ trợ.");
    private async Task<CustomerIdentityLease> Check(int store,Guid visitor,Guid conversation,CancellationToken ct,string? audit=null) {
        await access.Store(ct);var lease=storage.RequireIdentity(store,visitor,conversation,audit);
        if(!await access.VerifierAllowed(lease.Issuer,store,ct))throw Required();
        var customer=await db.Customers.IgnoreQueryFilters().AsNoTracking().Where(c=>c.Id==lease.CustomerId&&c.StoreId==store&&c.IsActive&&!c.IsDeleted).Select(c=>new{c.Id,c.Phone}).SingleOrDefaultAsync(ct);
        if(customer is null)throw Required();
        if(lease.ProtectedVerifiedPhone is not null){string phone;try{phone=Codes.Unprotect(lease.ProtectedVerifiedPhone);}catch(CryptographicException){storage.InvalidateIdentity(store,visitor,conversation,lease.Generation,"phone_snapshot_invalid");throw Required();}
            if(!string.Equals(phone,customer.Phone,StringComparison.Ordinal)){storage.InvalidateIdentity(store,visitor,conversation,lease.Generation,"phone_changed");throw Required();}}
        return lease;
    }
    private async Task Finish(int store,Guid visitor,Guid conversation,CustomerIdentityLease before,CancellationToken ct) {
        var after=await Check(store,visitor,conversation,ct);if(after!=before)throw Required();
    }
    public async Task<object> Status(int store,Guid visitor,Guid conversation,CancellationToken ct) {
        try{var proof=await Check(store,visitor,conversation,ct);return new{verified=true,expiresAt=(DateTimeOffset?)proof.ExpiresAt};}
        catch(AssistantException e)when(e.Code=="customer_verification_required"){return new{verified=false,expiresAt=(DateTimeOffset?)null};}
    }
    public async Task<object> Customers(CustomerStaff staff,Guid conversation,string query,CancellationToken ct) {
        storage.StaffGet(staff,conversation);query=query?.Trim()??"";if(query.Length is <3 or >50)throw AssistantException.Invalid();
        var rows=await db.Customers.IgnoreQueryFilters().AsNoTracking().Where(c=>c.StoreId==staff.StoreId&&!c.IsDeleted&&c.IsActive&&(c.Code==query||c.Phone==query)).OrderBy(c=>c.Id).Take(11).Select(c=>new{c.Id,c.Name,c.Code,c.Phone}).ToArrayAsync(ct);
        await access.Verifier(ct);return rows.Take(10).Select(c=>new{c.Id,c.Name,c.Code,phone=Mask(c.Phone)}).ToArray();
    }
    private static string? Mask(string? text)=>string.IsNullOrEmpty(text)?null:"••••"+text[^Math.Min(4,text.Length)..];
    public async Task<CustomerIdentityCode> Issue(CustomerStaff staff,Guid conversation,CustomerIdentityIssue input,CancellationToken ct) {
        var customer=await db.Customers.IgnoreQueryFilters().AsNoTracking().Where(c=>c.Id==input.CustomerId&&c.StoreId==staff.StoreId&&!c.IsDeleted&&c.IsActive).Select(c=>new{c.Id,c.Phone}).SingleOrDefaultAsync(ct);
        if(customer is null)throw new AssistantException(404,"customer_not_found","Không tìm thấy hồ sơ khách hợp lệ tại cửa hàng.");
        var fresh=await access.Verifier(ct);if(fresh.UserId!=staff.UserId||fresh.StoreId!=staff.StoreId)throw AssistantException.Denied();
        var target=storage.IdentityChannelTarget(fresh,conversation);string? phone=null;
        if(target is not null){if(string.IsNullOrWhiteSpace(customer.Phone))throw new AssistantException(422,"customer_phone_required","Hồ sơ khách cần có số điện thoại đã được nhân viên xác minh.");phone=Codes.Protect(customer.Phone);}
        return storage.IssueIdentity(fresh,conversation,input,Codes,phone);
    }
    public async Task<(string Reply,Guid? Generation)?> ChannelReply(CustomerChannelConnection connection,CustomerChannelPrivateTurn turn,CancellationToken ct) {
        var target=storage.RequireChannelIdentityTarget(connection,turn.StoreId,turn.VisitorId,turn.ConversationId);
        if(VerificationCodeCandidate(turn.Message)){
            (int Customer,int Issuer,Guid Generation,string? ProtectedPhone) pending;try{pending=storage.PendingIdentity(turn.StoreId,turn.VisitorId,turn.ConversationId);}catch(AssistantException){return ("Mã xác minh không hợp lệ, đã hết hạn hoặc bị khóa. Vui lòng nhờ nhân viên kiểm tra.",null);}
            var currentPhone=await db.Customers.IgnoreQueryFilters().AsNoTracking().Where(c=>c.Id==pending.Customer&&c.StoreId==turn.StoreId&&c.IsActive&&!c.IsDeleted).Select(c=>c.Phone).SingleOrDefaultAsync(ct);
            string? verifiedPhone=null;try{if(pending.ProtectedPhone is not null)verifiedPhone=Codes.Unprotect(pending.ProtectedPhone);}catch(CryptographicException){}
            if(string.IsNullOrWhiteSpace(verifiedPhone)||!string.Equals(verifiedPhone,currentPhone,StringComparison.Ordinal)){
                storage.InvalidateIdentity(turn.StoreId,turn.VisitorId,turn.ConversationId,pending.Generation,"phone_changed");
                return ("Số điện thoại hồ sơ đã thay đổi. Vui lòng nhờ nhân viên xác minh lại trước khi tra cứu.",null);
            }
            var accepted=storage.RedeemIdentity(turn.StoreId,turn.VisitorId,turn.ConversationId,new(turn.RequestId,turn.Message));
            return (accepted?"Đã xác minh thành công. Bạn có thể hỏi về đơn hàng gần đây, điểm tích lũy hoặc voucher đã được cấp.":"Mã xác minh không hợp lệ, đã hết hạn hoặc bị khóa. Vui lòng nhờ nhân viên kiểm tra.",null);
        }
        if(!PrivateQuery(turn.Message))return null;
        var active=storage.ActiveChannelIdentityGeneration(connection,turn.ConversationId,target);
        if(active is null)return ("Để bảo mật thông tin, bạn cần nhờ nhân viên xác minh số điện thoại và cấp mã trong cuộc trò chuyện này trước khi tra cứu.",null);
        CustomerIdentityLease proof;try{proof=await Check(turn.StoreId,turn.VisitorId,turn.ConversationId,ct);}catch(AssistantException){return ("Quyền tra cứu đã hết hạn hoặc thông tin hồ sơ đã thay đổi. Vui lòng nhờ nhân viên xác minh lại.",null);}
        if(proof.Generation!=active||!storage.ChannelIdentityActive(connection,turn.ConversationId,target.Peer,proof.Generation))return ("Quyền tra cứu đã hết hạn hoặc thay đổi. Vui lòng nhờ nhân viên xác minh lại.",null);
        var canonical=AssistantCatalogSearch.Canonical(turn.Message);string reply;
        if(System.Text.RegularExpressions.Regex.IsMatch(canonical,@"\b(?:don|lich su mua|kiem tra don|tra cuu don)\b")) {
            var result=await OrderList(turn.StoreId,turn.VisitorId,turn.ConversationId,0,ct);using var doc=JsonDocument.Parse(JsonSerializer.Serialize(result));var items=doc.RootElement.GetProperty("items");
            if(items.GetArrayLength()==0)reply="Mình chưa thấy đơn mua hoàn tất trong 90 ngày gần đây.";
            else {var lines=new List<string>{"Đơn mua gần đây:"};foreach(var item in items.EnumerateArray().Take(5))lines.Add($"• {item.GetProperty("number").GetString()} — {item.GetProperty("total").GetDecimal():N0}đ, {item.GetProperty("status").GetString()}, {item.GetProperty("payment").GetString()}");reply=string.Join("\n",lines);}
        } else {
            var result=await Rewards(turn.StoreId,turn.VisitorId,turn.ConversationId,ct);using var doc=JsonDocument.Parse(JsonSerializer.Serialize(result));var reward=doc.RootElement.GetProperty("reward");
            if(reward.GetProperty("status").GetString() is "unavailable" or "disabled")reply=reward.TryGetProperty("message",out var msg)?msg.GetString()!:"Chưa đọc được quyền lợi. Nhờ nhân viên kiểm tra giúp mình.";
            else {
                var lines=new List<string>{$"Số dư tích lũy: {reward.GetProperty("balanceAmount").GetDecimal():N0}đ.",$"Điểm hiện có: {reward.GetProperty("points").GetDecimal():N0}.",$"Số voucher có thể đổi: {reward.GetProperty("redeemableVoucherCount").GetInt32()}."};
                if(reward.TryGetProperty("issuedVouchers",out var vouchers)&&vouchers.GetArrayLength()>0){lines.Add("Voucher đã cấp còn khả dụng:");foreach(var voucher in vouchers.EnumerateArray().Take(5))lines.Add($"• {voucher.GetProperty("voucherCode").GetString()} — {voucher.GetProperty("value").GetDecimal():N0}đ");}
                reply=string.Join("\n",lines);
            }
        }
        await Finish(turn.StoreId,turn.VisitorId,turn.ConversationId,proof,ct);
        return (reply,proof.Generation);
    }
    public async Task<bool> ChannelReplyStillAuthorized(CustomerChannelConnection connection,CustomerChannelDispatch dispatch,CancellationToken ct) {
        if(dispatch.IdentityGeneration is not Guid generation)return true;
        try{var visitor=storage.ChannelVisitor(connection,dispatch.ConversationId);var target=storage.RequireChannelIdentityTarget(connection,connection.StoreId,visitor,dispatch.ConversationId);
            if(!storage.ChannelIdentityActive(connection,dispatch.ConversationId,target.Peer,generation))return false;
            var lease=await Check(connection.StoreId,visitor,dispatch.ConversationId,ct);return lease.Generation==generation;}
        catch(AssistantException){return false;}
    }
    public async Task<object> Redeem(int store,Guid visitor,Guid conversation,CustomerIdentityRedeem input,CancellationToken ct) {
        var pending=storage.PendingIdentity(store,visitor,conversation);
        if(!await access.VerifierAllowed(pending.Issuer,store,ct)||!await db.Customers.IgnoreQueryFilters().AsNoTracking().AnyAsync(c=>c.Id==pending.Customer&&c.StoreId==store&&c.IsActive&&!c.IsDeleted,ct))throw Required();
        if(!storage.RedeemIdentity(store,visitor,conversation,input))throw new AssistantException(403,"customer_code_invalid","Mã không hợp lệ, đã dùng, hết hạn hoặc bị khóa sau 5 lần sai. Nhờ nhân viên cấp lại mã giúp mình.");
        return await Status(store,visitor,conversation,ct);
    }
    private IQueryable<Order> Orders(int store,int customer)=>db.Orders.IgnoreQueryFilters().AsNoTracking().Where(o=>o.StoreId==store&&!o.IsDeleted&&o.CustomerId==customer&&o.CompletedAtUtc!=null&&o.CompletedAtUtc>=clock.GetUtcNow().UtcDateTime.AddDays(-90)&&(o.Status==OrderStatus.Completed||o.Status==OrderStatus.Voided||o.Status==OrderStatus.Refunded));
    public async Task<object> OrderList(int store,Guid visitor,Guid conversation,int page,CancellationToken ct) {
        if(page is <0 or >19)throw AssistantException.Invalid();var proof=await Check(store,visitor,conversation,ct,"orders_read");
        var rows=await Orders(store,proof.CustomerId).OrderByDescending(o=>o.CompletedAtUtc).ThenByDescending(o=>o.Id).Skip(page*10).Take(11).Select(o=>new{o.OrderNumber,o.CompletedAtUtc,o.GrandTotal,o.Status,o.PaymentStatus}).ToArrayAsync(ct);
        await Finish(store,visitor,conversation,proof,ct);
        return new{storeId=store,expiresAt=proof.ExpiresAt,readAt=clock.GetUtcNow(),page,hasMore=rows.Length>10,items=rows.Take(10).Select(o=>new{number=o.OrderNumber,completedAt=o.CompletedAtUtc,total=o.GrandTotal,status=StatusName(o.Status),payment=PaymentName(o.PaymentStatus)}),notice="Đơn mua gắn với hồ sơ đã xác minh trong 90 ngày. Trạng thái bán hàng không xác nhận đã giao hàng."};
    }
    public async Task<object> OrderDetail(int store,Guid visitor,Guid conversation,string number,CancellationToken ct) {
        if(string.IsNullOrWhiteSpace(number)||number.Length>30||number.Any(char.IsControl))throw AssistantException.Invalid();
        var proof=await Check(store,visitor,conversation,ct,"order_read");
        var row=await Orders(store,proof.CustomerId).Where(o=>o.OrderNumber==number).Select(o=>new{o.Id,o.OrderNumber,o.CompletedAtUtc,o.GrandTotal,o.DiscountTotal,o.OrderDiscount,o.Status,o.PaymentStatus}).SingleOrDefaultAsync(ct);
        if(row is null)throw new AssistantException(404,"customer_order_not_found","Không tìm thấy đơn được phép xem trong 90 ngày gần đây.");
        var lines=await db.OrderLines.IgnoreQueryFilters().AsNoTracking().Where(l=>l.StoreId==store&&!l.IsDeleted&&l.OrderId==row.Id).OrderBy(l=>l.Id).Take(201).Select(l=>new{name=l.ItemName,unit=l.SellingUnitName??l.UnitName,quantity=l.Quantity,unitPrice=l.UnitPrice,total=l.LineTotal}).ToArrayAsync(ct);
        await Finish(store,visitor,conversation,proof,ct);
        return new{storeId=store,expiresAt=proof.ExpiresAt,readAt=clock.GetUtcNow(),number=row.OrderNumber,completedAt=row.CompletedAtUtc,total=row.GrandTotal,lineDiscount=row.DiscountTotal,orderDiscount=row.OrderDiscount,status=StatusName(row.Status),payment=PaymentName(row.PaymentStatus),lines=lines.Take(200),truncated=lines.Length>200};
    }
    public async Task<object> Rewards(int store,Guid visitor,Guid conversation,CancellationToken ct) {
        var proof=await Check(store,visitor,conversation,ct,"rewards_read");object result;
        try{
            if(!await rewards.IsProgramEnabledAsync(ct))result=new{status="disabled",message="Cửa hàng chưa bật chương trình tích điểm."};
            else{var balance=await rewards.GetBalanceAsync(proof.CustomerId,ct);if(balance.CustomerId!=proof.CustomerId)throw new InvalidOperationException();
                var issued=await rewards.GetAvailableVouchersAsync(proof.CustomerId,ct);
                result=new{status=balance.IsPointEligible?"available":"ineligible",balanceAmount=balance.BalanceAmount,points=balance.AvailablePoints,moneyPerPoint=balance.MoneyPerPoint,pointsPerVoucher=balance.PointsPerVoucher,redeemableVoucherCount=balance.AvailableVoucherCount,voucherValue=balance.VoucherValue,issuedVouchers=issued.Select(v=>new{voucherCode=v.VoucherCode,value=v.Value,status=v.Status,issuedAtUtc=v.IssuedAtUtc})};}
        }catch(InvalidOperationException){result=new{status="unavailable",message="Chưa đọc được quyền lợi. Nhờ nhân viên kiểm tra giúp mình."};}
        await Finish(store,visitor,conversation,proof,ct);return new{storeId=store,expiresAt=proof.ExpiresAt,readAt=clock.GetUtcNow(),reward=result,notice="Chỉ tra cứu; đổi phiếu hoặc điều chỉnh cần nhân viên xác nhận. Số dư tích lũy và điểm là hai giá trị khác nhau."};
    }
    private static string StatusName(OrderStatus value)=>value switch{OrderStatus.Completed=>"Đã chốt bán",OrderStatus.Voided=>"Đã hủy sau bán",OrderStatus.Refunded=>"Đã trả hàng/hoàn tiền",_=>"Chưa xác định"};
    private static string PaymentName(PaymentStatus value)=>value switch{PaymentStatus.Paid=>"Đã thanh toán",PaymentStatus.Unpaid=>"Chưa thanh toán",PaymentStatus.PartiallyPaid=>"Thanh toán một phần",PaymentStatus.Voided=>"Đã hủy thanh toán",PaymentStatus.Refunded=>"Đã hoàn tiền",_=>"Chưa xác định"};
}
