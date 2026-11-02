using System.Text.Json;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Services.Banking;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Storefront;

public sealed partial class StorefrontFulfillmentService
{
    internal static IQueryable<BankVerifiedReceipt> BankReceiptsForOrder(GaoApp.Infrastructure.Data.AppDbContext context, int store, int orderId)
        => StorefrontOrderProofLocks.BankReceiptsForOrder(context, store, orderId);
    private async Task<bool> HasRetainedFunds(int orderId, CancellationToken ct)
    {
        return await db.Set<StorefrontPaymentAllocation>().AnyAsync(x => x.StoreId == Store && x.StorefrontOrderId == orderId && x.Amount > 0, ct) ||
            await db.Set<StorefrontCodCollection>().AnyAsync(x => x.StoreId == Store && x.StorefrontOrderId == orderId && x.Amount > 0, ct) ||
            await db.Set<StorefrontCodReceipt>().AnyAsync(x => x.StoreId == Store && x.StorefrontOrderId == orderId && x.Amount > 0, ct) ||
            await BankReceiptsForOrder(db, Store, orderId).AnyAsync(x =>
                x.AssetConfidence == BankReceiptAssetConfidence.CertifiedDistinct && x.Amount > 0, ct);
    }
    internal Task LockCashEvidence(int cashId, CancellationToken ct) => StorefrontOrderProofLocks.CashAsync(db, Store, cashId, ct);
    internal Task LockQrEvidence(int id, CancellationToken ct, int? incomingGlobalReceiptId = null, int? incomingCashId = null, int? incomingExpenseId = null)
        => StorefrontOrderProofLocks.LockAsync(db, Store, id, ct, incomingGlobalReceiptId, incomingCashId, incomingExpenseId);
    private async Task RequireCurrentPaymentProof(StorefrontOrder order, CancellationToken ct)
    {
        Need(PaymentReady(order), "Khoản thanh toán đang cần đối soát; chưa được chuẩn bị/giao.");
        if (order.PaymentMethod == StorefrontPaymentMethod.Cod) return;
        var intents = await db.Set<StorefrontPaymentIntent>().AsNoTracking().Where(x => x.StoreId == Store && x.StorefrontOrderId == order.Id).ToListAsync(ct);
        Need(intents.Count == 1, "Thiếu nguồn thanh toán QR duy nhất của đơn.");
        var intent = intents.Single(); var binding = JsonSerializer.Deserialize<StorefrontBankBinding>(intent.BindingSnapshotJson, Json)!;
        var allocations = await db.Set<StorefrontPaymentAllocation>().AsNoTracking().Where(x => x.StoreId == Store && x.StorefrontOrderId == order.Id && x.PaymentIntentId == intent.Id).ToListAsync(ct);
        Need(allocations.Count == 1 && allocations[0].Amount == order.TotalDue && intent.AmountDue == order.TotalDue, "Khoản tiền đã phân bổ chưa đủ đúng đơn.");
        var local = await db.Set<StorefrontBankReceipt>().AsNoTracking().SingleAsync(x => x.StoreId == Store && x.Id == allocations[0].BankReceiptId, ct);
        var global = await db.Set<BankVerifiedReceipt>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == local.GlobalBankReceiptId && x.OriginStoreId == Store, ct);
        Need(global is not null && global.AssetConfidence == BankReceiptAssetConfidence.CertifiedDistinct && !global.NeedsReview &&
            global.Channel == BankPaymentChannel.Storefront && global.ProviderReference == intent.ProviderReference && global.AccountIdentity == binding.AccountIdentity &&
            global.BankAccountId == binding.BankAccountId && global.Amount == intent.AmountDue && global.TimePrecision != BankTimePrecision.Unknown && global.OccurredAtUtc.HasValue &&
            local.IsVerified && !local.NeedsReview, "Chứng cứ nhận tiền QR chưa đủ điều kiện hiện tại.");
        Need(await db.Set<BankReceiptConsumption>().AnyAsync(x => x.BankVerifiedReceiptId == global!.Id && x.OriginStoreId == Store && x.Channel == BankPaymentChannel.Storefront && x.StorefrontAllocationId == allocations[0].Id, ct), "Thiếu nguồn sử dụng khoản tiền riêng của đơn.");
        Need(!await db.Set<BankReceiptConflict>().AnyAsync(x => x.BankVerifiedReceiptId == global!.Id, ct) &&
            !await db.Set<StorefrontUnattributedBankReceipt>().AnyAsync(x => x.StoreId == Store && x.PaymentIntentId == intent.Id, ct) &&
            await db.Set<StorefrontQrProcessing>().AnyAsync(x => x.StoreId == Store && x.PaymentIntentId == intent.Id && x.State == (int)StorefrontQrState.Paid && x.ReviewCode == null, ct), "Có bằng chứng tiền cần đối soát; dừng giao/ghi bán.");
    }
}
