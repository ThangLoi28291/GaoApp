using System.Text.Json;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Services.Banking;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Storefront;

public sealed partial class StorefrontBankProcessor
{
    private async Task Allocate(int intentId,int globalReceiptId,bool bindingMismatch,CancellationToken ct)
    {
        // The ledger commit is complete. This separate transaction may fail without losing actual bank evidence.
        var key=await db.Set<BankVerifiedReceipt>().AsNoTracking().SingleAsync(x=>x.Id==globalReceiptId,ct);
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await GlobalBankReceiptLedger.LockAsync(db,"GaoApp:BankReceipt:"+GlobalBankReceiptLedger.Hash(key.Provider+"|"+key.AccountIdentity+"|"+key.ExternalTransactionId),ct);
        var intent=await db.Set<StorefrontPaymentIntent>().SingleAsync(x=>x.StoreId==Store&&x.Id==intentId,ct);
        await GlobalBankReceiptLedger.LockAsync(db,$"GaoApp:Storefront:Order:{Store}:{intent.StorefrontOrderId}",ct);
        var receipt=await db.Set<BankVerifiedReceipt>().SingleAsync(x=>x.Id==globalReceiptId,ct);await db.Entry(receipt).ReloadAsync(ct);
        var order=await db.StorefrontOrders.SingleAsync(x=>x.StoreId==Store&&x.Id==intent.StorefrontOrderId,ct);await db.Entry(order).ReloadAsync(ct);
        var state=await db.Set<StorefrontQrProcessing>().SingleAsync(x=>x.StoreId==Store&&x.PaymentIntentId==intentId,ct);await db.Entry(state).ReloadAsync(ct);
        var binding=JsonSerializer.Deserialize<StorefrontBankBinding>(intent.BindingSnapshotJson,Json)!;
        var owned=receipt.OriginStoreId==Store&&receipt.Channel==BankPaymentChannel.Storefront&&receipt.ProviderReference==intent.ProviderReference&&
            receipt.AccountIdentity==binding.AccountIdentity&&receipt.BankAccountId==binding.BankAccountId;
        var existing=await db.Set<BankReceiptConsumption>().AsNoTracking().SingleOrDefaultAsync(x=>x.BankVerifiedReceiptId==receipt.Id,ct);
        var review=bindingMismatch||!owned||receipt.AssetConfidence!=BankReceiptAssetConfidence.CertifiedDistinct||receipt.NeedsReview||order.PaymentState==StorefrontPaymentState.Review||state.State==(int)StorefrontQrState.Review;
        // At most two minutes of provider clock skew; never substitute server observation for unknown bank time.
        if(receipt.TimePrecision==BankTimePrecision.Unknown||!receipt.OccurredAtUtc.HasValue)review=true;
        else if(receipt.TimePrecision==BankTimePrecision.Timestamp)
            review|=receipt.OccurredAtUtc.Value<order.CreatedAtUtc.AddMinutes(-2)||receipt.OccurredAtUtc.Value>receipt.VerifiedAtUtc.AddMinutes(2)||receipt.OccurredAtUtc.Value>=intent.ExpiresAtUtc;
        else
        {
            var dayStart=receipt.OccurredAtUtc.Value;var dayEnd=dayStart.AddDays(1);
            review|=dayEnd<=order.CreatedAtUtc||dayStart>receipt.VerifiedAtUtc.AddMinutes(2)||
                // A fresh authenticated lookup observed before expiry bounds this bank day by its observation.
                receipt.VerifiedAtUtc>intent.ExpiresAtUtc&&dayEnd>intent.ExpiresAtUtc;
        }
        if(existing is not null)
        {
            if(existing.Channel!=BankPaymentChannel.Storefront||existing.OriginStoreId!=Store||review)
                await MarkReview(intent,state,"BANK_RECEIPT_ALREADY_BOUND_OR_REVIEW",ct);
            if(order.Phase==StorefrontOrderPhase.Cancelled){await StorefrontRefundObligations.EnsureAsync(db,order,clock,ct);await db.SaveChangesAsync(ct);}
            await tx.CommitAsync(ct);return;
        }
        review|=await db.Set<BankVerifiedReceipt>().AsNoTracking().AnyAsync(x=>x.OriginStoreId==Store&&x.ProviderReference==intent.ProviderReference&&x.NeedsReview,ct);
        StorefrontPaymentDecision? decision=null;
        if(owned&&receipt.Amount>0)
        {
            decision=StorefrontPaymentPolicy.Evaluate(new(Store,order.Id,intent.ProviderReference,binding.BankAccountId,binding.ReceivingLegalEntityId,
                intent.AmountDue,binding.SourceLegalEntityIds.ToHashSet(),binding.SourceLegalEntityIds.ToHashSet()),
                new(Store,order.Id,intent.ProviderReference,binding.BankAccountId,binding.ReceivingLegalEntityId,receipt.ExternalTransactionId,receipt.Amount,true),
                order.Phase,new HashSet<string>(),review,order.PaymentState==StorefrontPaymentState.Confirmed);
            review|=decision.Disposition!=StorefrontPaymentDisposition.Apply;
        }
        else review=true;
        if(review)
        {
            if(owned)
            {
                receipt.NeedsReview=true;receipt.ReviewCode??=decision?.ReviewCode??"RECEIPT_BINDING_TIME_OR_REVIEW";
                if(receipt.OccurredAtUtc.HasValue)await StoreReceipt(receipt,binding,ct);
            }
            await MarkReview(intent,state,decision?.ReviewCode??"RECEIPT_REVIEW_REQUIRED",ct);
            if(decision?.CreateRefundReview==true)
            {
                order.RefundState=StorefrontRefundState.Requested;
                // Ensure may already have created this actual source's typed obligation
                // while handling another receipt from the same completed bank scan.
                // Never add a second unlinked legacy liability beside that source.
                if(!await db.Set<StorefrontRefundFunding>().IgnoreQueryFilters().AnyAsync(x=>x.GlobalBankReceiptId==receipt.Id,ct)&&
                    !await db.Set<StorefrontRefund>().AnyAsync(x=>x.StoreId==Store&&x.StorefrontOrderId==order.Id&&x.Reason=="LATE_BANK_RECEIPT:"+receipt.Id,ct))
                    db.Add(new StorefrontRefund{StoreId=Store,StorefrontOrderId=order.Id,State=StorefrontRefundState.Requested,Reason="LATE_BANK_RECEIPT:"+receipt.Id,
                        UnallocatedReceiptAmount=receipt.Amount,Amount=receipt.Amount});
                await db.SaveChangesAsync(ct);
            }
            if(order.Phase==StorefrontOrderPhase.Cancelled){await StorefrontRefundObligations.EnsureAsync(db,order,clock,ct);await db.SaveChangesAsync(ct);}
            await tx.CommitAsync(ct);return;
        }
        var bankReceipt=await StoreReceipt(receipt,binding,ct);
        var allocation=new StorefrontPaymentAllocation {StoreId=Store,StorefrontOrderId=order.Id,PaymentIntentId=intent.Id,BankReceiptId=bankReceipt.Id,
            BankAccountId=binding.BankAccountId,ReceivingLegalEntityId=binding.ReceivingLegalEntityId,Amount=receipt.Amount,ReceiptVerified=true};
        db.Add(allocation);await db.SaveChangesAsync(ct);
        db.Add(new BankReceiptConsumption{BankVerifiedReceiptId=receipt.Id,OriginStoreId=Store,Channel=BankPaymentChannel.Storefront,StorefrontAllocationId=allocation.Id});
        // The order's SQL proof guard reads this exact consumption. Save it before changing
        // payment state; the encompassing transaction still commits all business effects once.
        await db.SaveChangesAsync(ct);
        order.Phase=StorefrontOrderPhase.AwaitingPreparation;order.PaymentState=StorefrontPaymentState.Confirmed;order.Revision++;
        intent.IsActive=false;
        state.State=(int)StorefrontQrState.Paid;state.ReviewCode=null;
        var eventId=Guid.NewGuid();var payload=JsonSerializer.Serialize(new{PaymentIntentId=intent.Id,BankReceiptId=bankReceipt.Id,GlobalBankReceiptId=receipt.Id,receipt.Amount,receipt.TimePrecision,receipt.BankBusinessDate,receipt.OccurredAtUtc},Json);
        db.Add(new StorefrontEvent{StoreId=Store,EventId=eventId,StorefrontOrderId=order.Id,Revision=order.Revision,Kind="QrPaymentConfirmed",PayloadJson=payload});
        db.Add(new StorefrontOutboxMessage{StoreId=Store,EventId=eventId,StorefrontOrderId=order.Id,Revision=order.Revision,Kind="QrPaymentConfirmed",PayloadJson=payload});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    private async Task<StorefrontBankReceipt> StoreReceipt(BankVerifiedReceipt receipt,StorefrontBankBinding binding,CancellationToken ct)
    {
        var result=await db.Set<StorefrontBankReceipt>().SingleOrDefaultAsync(x=>x.StoreId==Store&&x.GlobalBankReceiptId==receipt.Id,ct);
        if(result is null)
        {
            result=new(){StoreId=Store,GlobalBankReceiptId=receipt.Id,Provider=receipt.Provider,BankAccountId=binding.BankAccountId,ReceivingLegalEntityId=binding.ReceivingLegalEntityId,
                ExternalTransactionId=receipt.ExternalTransactionId,EvidenceFingerprint=receipt.Fingerprint,ProtectedEvidence=receipt.ProtectedEvidence,
                Amount=receipt.Amount,OccurredAtUtc=DateTime.SpecifyKind(receipt.OccurredAtUtc!.Value,DateTimeKind.Utc),IsVerified=true,NeedsReview=receipt.NeedsReview};
            db.Add(result);await db.SaveChangesAsync(ct);
        }
        else if(receipt.NeedsReview&&!result.NeedsReview){result.NeedsReview=true;await db.SaveChangesAsync(ct);}
        return result;
    }
}
