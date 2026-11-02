using System.Globalization;
using System.Text.Json;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Services.Banking;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.Acb;

public sealed partial class AcbPaymentService
{
    private async Task<int?> NotificationProof(PosPaymentQrRequest qr,BankNotificationReceipt notification,CancellationToken ct)
    {
        var parsed=BankNotificationParser.Parse(notification.Content)??throw new InvalidOperationException("Thông báo chưa đủ bằng chứng để đối soát.");
        if(parsed.QrCode is string web&&System.Text.RegularExpressions.Regex.IsMatch(web,"^W[0-9]{12}$"))
            throw new InvalidOperationException("Khoản này thuộc website; không ghi thành tiền POS.");
        if(qr.PaymentId is int alreadyPaid&&notification.PaymentId==alreadyPaid)
            return await db.OrderPayments.Where(x=>x.StoreId==StoreId&&x.Id==alreadyPaid).Select(x=>x.BankVerifiedReceiptId).SingleAsync(ct);
        var bank=await db.StoreBankAccounts.AsNoTracking().SingleAsync(x=>x.StoreId==StoreId&&x.Id==qr.BankAccountId,ct);
        var device=await db.Set<BankNotificationDevice>().AsNoTracking().SingleOrDefaultAsync(x=>x.StoreId==StoreId&&x.Id==notification.DeviceId,ct);
        if(device?.Enabled!=true||device.BankAccountFingerprint!=BankNotificationParser.AccountFingerprint(bank))
            throw new InvalidOperationException("Nguồn thiết bị hoặc tài khoản thông báo đã thay đổi; cần đối soát.");
        var identity=GlobalBankReceiptLedger.AccountIdentity(bank.BankCode,bank.AccountNumber);
        // Notification references and provider transactionNumber have no assumed mapping, including FT.
        var configuredProof=bankingRegistration?.Get(StoreId);
        var currentMappingProof=configuredProof is {NotificationIdentityMappingVerified:true}&&configuredProof.AccountIdentity==identity;
        var unprovenExposure=!currentMappingProof&&await db.Set<BankPaymentReference>().AsNoTracking().AnyAsync(x=>x.AccountIdentity==identity&&x.Channel==BankPaymentChannel.Storefront&&!x.NotificationIdentityMappingVerified,ct);
        var mappingProven=currentMappingProof||!unprovenExposure&&await db.Set<BankPaymentReference>().AsNoTracking().AnyAsync(x=>x.AccountIdentity==identity&&x.NotificationIdentityMappingVerified,ct);
        var nativeId=parsed.TransactionCode.StartsWith("MGD:",StringComparison.Ordinal)
            ?parsed.TransactionCode+":"+(parsed.BankOccurredAtUtc??notification.PostedAtUtc).AddHours(7).ToString("yyyyMMdd",CultureInfo.InvariantCulture):parsed.TransactionCode;
        var globalId=mappingProven?parsed.TransactionCode:"PHONE:"+nativeId;
        var ledger=new GlobalBankReceiptLedger(db,protocol.Core);
        await ledger.RegisterPosAsync(qr.Id,qr.RequestCode,ct,providerReference:false);
        var proof=await ledger.RecordVerifiedAsync(new(GlobalBankReceiptLedger.AccountSnapshot(bank),qr.RequestCode,BankPaymentChannel.Pos,globalId,
            notification.Amount,"COMPLETED",parsed.BankOccurredAtUtc?.ToString("O",CultureInfo.InvariantCulture)??"",notification.Content,
            BankExternalIdentityKind.BankNotification,mappingProven,unprovenExposure),ct);
        if(proof.Conflict)throw new InvalidOperationException("Bằng chứng ngân hàng đã thuộc nguồn khác hoặc chưa đủ ngày nhận; cần đối soát trước khi ghi tiền POS.");
        return proof.Id;
    }
    private async Task ClaimNotificationProof(int receiptId,int paymentId,CancellationToken ct)
    {
        var proof=await db.Set<BankVerifiedReceipt>().AsNoTracking().SingleAsync(x=>x.Id==receiptId,ct);
        await GlobalBankReceiptLedger.LockAsync(db,"GaoApp:BankReceipt:"+GlobalBankReceiptLedger.Hash(proof.Provider+"|"+proof.AccountIdentity+"|"+proof.ExternalTransactionId),ct);
        proof=await db.Set<BankVerifiedReceipt>().AsNoTracking().SingleAsync(x=>x.Id==receiptId,ct);
        if(GlobalBankReceiptLedger.BlocksPosAllocation(proof)||proof.OriginStoreId!=StoreId||proof.Channel!=BankPaymentChannel.Pos)throw new InvalidOperationException("Khoản ngân hàng đang cần đối soát.");
        var payment=await db.OrderPayments.SingleAsync(x=>x.StoreId==StoreId&&x.Id==paymentId,ct);
        if(payment.Method!=GaoApp.Domain.Enums.PaymentMethod.BankTransfer||payment.Amount!=proof.Amount||payment.ReferenceCode!=proof.ProviderReference)
            throw new InvalidOperationException("Nguồn khoản thanh toán không khớp bằng chứng ngân hàng.");
        var prior=await db.Set<BankReceiptConsumption>().SingleOrDefaultAsync(x=>x.BankVerifiedReceiptId==receiptId,ct);
        if(prior is not null){if(prior.Channel!=BankPaymentChannel.Pos||prior.OriginStoreId!=StoreId||prior.PosPaymentId!=paymentId)throw new InvalidOperationException("Khoản tiền đã được sử dụng cho nguồn khác.");return;}
        db.Add(new BankReceiptConsumption{BankVerifiedReceiptId=receiptId,Channel=BankPaymentChannel.Pos,OriginStoreId=StoreId,PosPaymentId=paymentId});
    }
    private async Task LockNotificationProof(int receiptId,CancellationToken ct)
    {
        var proof=await db.Set<BankVerifiedReceipt>().AsNoTracking().SingleAsync(x=>x.Id==receiptId,ct);
        await GlobalBankReceiptLedger.LockAsync(db,"GaoApp:BankReceipt:"+GlobalBankReceiptLedger.Hash(proof.Provider+"|"+proof.AccountIdentity+"|"+proof.ExternalTransactionId),ct);
        proof=await db.Set<BankVerifiedReceipt>().AsNoTracking().SingleAsync(x=>x.Id==receiptId,ct);
        if(GlobalBankReceiptLedger.BlocksPosAllocation(proof)||proof.OriginStoreId!=StoreId||proof.Channel!=BankPaymentChannel.Pos)throw new InvalidOperationException("Khoản ngân hàng đang cần đối soát.");
    }
}
