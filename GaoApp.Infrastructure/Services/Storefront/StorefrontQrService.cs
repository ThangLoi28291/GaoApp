using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Storefront;

public sealed class StorefrontQrService(AppDbContext db,TimeProvider clock) : IStorefrontQrService
{
    private async Task<(StorefrontOrder Order,StorefrontPaymentIntent Intent,StorefrontQrProcessing State)?> Find(string token,CancellationToken ct)
    {
        if(db.CurrentStoreId is not >0||!Regex.IsMatch(token??"","^[A-Za-z0-9_-]{43}$"))return null;
        try{var decoded=WebEncoders.Base64UrlDecode(token!);if(decoded.Length!=32||WebEncoders.Base64UrlEncode(decoded)!=token)return null;}catch(FormatException){return null;}
        var store=db.CurrentStoreId.Value;if(!await db.Stores.AnyAsync(x=>x.Id==store&&x.IsActive,ct))return null;
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token!)));
        var order=await db.StorefrontOrders.AsNoTracking().SingleOrDefaultAsync(x=>x.StoreId==store&&x.GuestTokenHash==hash&&x.PaymentMethod==StorefrontPaymentMethod.Qr,ct);
        if(order is null)return null;
        var intent=await db.Set<StorefrontPaymentIntent>().AsNoTracking().Where(x=>x.StoreId==store&&x.StorefrontOrderId==order.Id).OrderByDescending(x=>x.IsActive).ThenByDescending(x=>x.Id).FirstOrDefaultAsync(ct);
        if(intent is null)return null;
        var state=await db.Set<StorefrontQrProcessing>().AsNoTracking().SingleOrDefaultAsync(x=>x.StoreId==store&&x.PaymentIntentId==intent.Id,ct);
        return state is null?null:(order,intent,state);
    }
    public async Task<StorefrontQrDto?> GetAsync(string guestToken,CancellationToken ct=default)
    {
        var found=await Find(guestToken,ct);if(found is null)return null;
        var(order,intent,state)=found.Value;var binding=JsonSerializer.Deserialize<StorefrontBankBinding>(intent.BindingSnapshotJson,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var reviewed=order.PaymentState==StorefrontPaymentState.Review||await db.Set<BankVerifiedReceipt>().AnyAsync(x=>x.OriginStoreId==order.StoreId&&x.ProviderReference==intent.ProviderReference&&x.NeedsReview,ct);
        var status=reviewed?StorefrontQrState.Review:(StorefrontQrState)state.State;
        if(status is not(StorefrontQrState.Paid or StorefrontQrState.Review)&&intent.ExpiresAtUtc<=clock.GetUtcNow().UtcDateTime)status=StorefrontQrState.Expired;
        if(order.Phase==StorefrontOrderPhase.Cancelled)status=StorefrontQrState.Closed;
        var message=status switch{StorefrontQrState.Paid=>"Ngân hàng đã xác minh khoản tiền. Xem trạng thái giao hàng và khoản hoàn ở thông tin đơn.",
            StorefrontQrState.Closed=>"Đơn đã hủy. Không chuyển thêm tiền hoặc dùng QR này. Cửa hàng vẫn kiểm tra tiền về muộn và theo dõi khoản cần hoàn riêng.",
            StorefrontQrState.Review=>"Khoản thanh toán cần cửa hàng đối soát. Giữ liên kết đơn và không chuyển thêm tiền.",
            StorefrontQrState.Expired=>"QR đã quá thời hạn. Không chuyển thêm tiền; cửa hàng đang kiểm tra giao dịch và lượng giữ.",
            StorefrontQrState.Queued=>"Đã lưu yêu cầu QR. Máy chủ ngân hàng đang chuẩn bị xử lý.",
            StorefrontQrState.Creating=>"Đang xác minh yêu cầu tạo QR. Mở lại vẫn dùng đúng yêu cầu này.",_=>"Chuyển đúng số tiền và kiểm tra tên người nhận. Trạng thái được máy chủ xác minh tự động."};
        return new(status,intent.ProviderReference,intent.AmountDue,binding.BankName,binding.BeneficiaryName,
            status==StorefrontQrState.Closed?"":state.VirtualAccount,status==StorefrontQrState.Closed?"":intent.ProviderReference,
            DateTime.SpecifyKind(intent.ExpiresAtUtc,DateTimeKind.Utc),status==StorefrontQrState.Pending&&state.QrPng is {Length:>0},message);
    }
    public async Task<byte[]?> ImageAsync(string guestToken,CancellationToken ct=default)
    {
        var found=await Find(guestToken,ct);if(found is null)return null;
        var(order,intent,state)=found.Value;
        if(order.Phase==StorefrontOrderPhase.Cancelled||order.PaymentState==StorefrontPaymentState.Review||state.State!=(int)StorefrontQrState.Pending||intent.ExpiresAtUtc<=clock.GetUtcNow().UtcDateTime||
            await db.Set<BankVerifiedReceipt>().AnyAsync(x=>x.OriginStoreId==order.StoreId&&x.ProviderReference==intent.ProviderReference&&x.NeedsReview,ct))return null;
        return state.QrPng;
    }
}
