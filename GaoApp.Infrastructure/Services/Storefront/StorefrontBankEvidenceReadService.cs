using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Storefront;

public sealed class StorefrontBankEvidenceReadService(AppDbContext db,ICurrentUser user,ICurrentStorePermissionService permissions):IStorefrontBankEvidenceReadService
{
    public async Task<StorefrontBankEvidenceRead> GetAsync(int orderId,CancellationToken ct=default)
    {
        var store=db.CurrentStoreId??0;
        if(store<=0||!user.IsAuthenticated||user.UserId is not >0||
            !await permissions.HasPermissionAsync(store,user.UserId.Value,StorefrontPermissions.ViewOrders,ct)||
            !await db.Stores.AnyAsync(x=>x.Id==store&&x.IsActive,ct))throw new ForbiddenAppException("Không có quyền xem bằng chứng đơn website.");
        var code=await db.StorefrontOrders.AsNoTracking().Where(x=>x.StoreId==store&&x.Id==orderId&&
            db.Set<StorefrontEvent>().Any(e=>e.StoreId==store&&e.StorefrontOrderId==x.Id&&e.Kind=="OrderCreated")).Select(x=>x.Code).SingleOrDefaultAsync(ct)
            ??throw new NotFoundAppException("Không tìm thấy đơn website.");
        var intents=await db.Set<StorefrontPaymentIntent>().AsNoTracking().Where(x=>x.StoreId==store&&x.StorefrontOrderId==orderId).OrderByDescending(x=>x.Id).Take(101).ToListAsync(ct);
        if(intents.Count>100)throw new ValidationAppException("Đơn có quá nhiều lần thanh toán; cần kiểm tra nguồn chứng cứ.");
        var ids=intents.Select(x=>x.Id).ToList();var refs=intents.Select(x=>x.ProviderReference).ToList();
        var states=await db.Set<StorefrontQrProcessing>().AsNoTracking().Where(x=>x.StoreId==store&&ids.Contains(x.PaymentIntentId)).ToListAsync(ct);
        var receipts=await db.Set<BankVerifiedReceipt>().AsNoTracking().Where(x=>x.OriginStoreId==store&&refs.Contains(x.ProviderReference)).OrderBy(x=>x.Id).Take(201).ToListAsync(ct);
        if(receipts.Count>200)throw new ValidationAppException("Đơn có quá nhiều chứng cứ ngân hàng; xem báo cáo đối soát để kiểm tra.");
        var receiptIds=receipts.Select(x=>x.Id).ToList();
        return new(code,intents.Select(x=>{var state=states.SingleOrDefault(s=>s.PaymentIntentId==x.Id);return new StorefrontBankIntentRead(x.ProviderReference,x.AmountDue,
                (StorefrontQrState)(state?.State??(int)StorefrontQrState.Queued),DateTime.SpecifyKind(x.ExpiresAtUtc,DateTimeKind.Utc),state?.ReviewCode);}).ToList(),
            receipts.Select(x=>new StorefrontBankReceiptRead(x.ExternalTransactionId,x.Amount,x.Status,x.TimePrecision,x.BankBusinessDate,
                x.OccurredAtUtc.HasValue?DateTime.SpecifyKind(x.OccurredAtUtc.Value,DateTimeKind.Utc):null,x.NeedsReview,x.ReviewCode,x.AssetConfidence)).ToList(),
            await db.Set<StorefrontBankObservation>().CountAsync(x=>x.StoreId==store&&ids.Contains(x.PaymentIntentId),ct),
            await db.Set<StorefrontUnattributedBankReceipt>().CountAsync(x=>x.StoreId==store&&ids.Contains(x.PaymentIntentId),ct),
            await db.Set<BankReceiptConflict>().CountAsync(x=>x.AttemptedStoreId==store&&(receiptIds.Contains(x.BankVerifiedReceiptId)||refs.Contains(x.AttemptedReference)),ct));
    }
}
