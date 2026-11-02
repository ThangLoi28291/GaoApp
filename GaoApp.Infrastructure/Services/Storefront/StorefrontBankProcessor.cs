using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Services.Banking;
using Microsoft.EntityFrameworkCore;
using static GaoApp.Infrastructure.Services.Banking.AcbProtocolCore;

namespace GaoApp.Infrastructure.Services.Storefront;

public sealed partial class StorefrontBankProcessor(AppDbContext db,AcbProtocolCore protocol,TimeProvider clock) : IStorefrontBankProcessor
{
    private int Store=>db.CurrentStoreId is >0?db.CurrentStoreId.Value:throw new ForbiddenAppException("Thiếu phạm vi ngân hàng.");
    private DateTime Now=>clock.GetUtcNow().UtcDateTime;
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public async Task ProcessCallbackAsync(JsonElement authenticatedPayload,CancellationToken ct=default)
    {
        // The existing ACB controller commits/authenticates the envelope first. Never route tenants from custom4.
        var references=Items(Path(authenticatedPayload,"requestParameters","request","requestParams","transactions"))
            .Select(value=>Text(Path(value,"transactionEntityAttribute"),"custom4")).Where(value=>Regex.IsMatch(value,"^W[0-9]{12}$")).Distinct().Take(1000).ToList();
        var mappings=await db.Set<BankPaymentReference>().AsNoTracking().Where(x=>x.Provider=="ACB"&&x.OriginStoreId==Store&&x.Channel==BankPaymentChannel.Storefront&&references.Contains(x.Reference))
            .Select(x=>new{x.Reference,Id=x.StorefrontPaymentIntentId!.Value}).ToListAsync(ct);
        foreach(var mapping in mappings)
        {
            var related=Items(Path(authenticatedPayload,"requestParameters","request","requestParams","transactions")).Where(value=>Text(Path(value,"transactionEntityAttribute"),"custom4")==mapping.Reference).ToList();
            var state=await db.Set<StorefrontQrProcessing>().SingleAsync(x=>x.StoreId==Store&&x.PaymentIntentId==mapping.Id,ct);
            foreach(var item in related)
                if(DateTime.TryParseExact(Text(item,"effectiveDate"),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var day)&&day.Year is >=2000 and <=9998)state.RequestedBankDate=day;
            if(related.Any(item=>Text(item,"transactionStatus")=="ERRORCORRECTED"||Text(item,"debitOrCredit")=="debit"))
            {
                var intent=await db.Set<StorefrontPaymentIntent>().SingleAsync(x=>x.StoreId==Store&&x.Id==mapping.Id,ct);await MarkReview(intent,state,"BANK_CORRECTION_NOTIFICATION",ct,authenticatedPayload);
            }
            await db.SaveChangesAsync(ct);await ProcessAsync(mapping.Id,ct);
        }
    }
    public async Task ProcessAsync(int paymentIntentId,CancellationToken ct=default)
    {
        if(!await db.Stores.AsNoTracking().AnyAsync(x=>x.Id==Store&&x.IsActive,ct)) return;
        var hint=await db.Set<StorefrontPaymentIntent>().AsNoTracking().SingleOrDefaultAsync(x=>x.StoreId==Store&&x.Id==paymentIntentId,ct);
        if(hint is null) return;
        await db.Database.OpenConnectionAsync(ct);
        var resource=$"GaoApp:Storefront:BankWorker:{Store}:{paymentIntentId}";
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={resource},@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=15000; IF @r<0 THROW 51141,'Bank processing is busy.',1;",ct);
            db.ChangeTracker.Clear();
            var intent=await db.Set<StorefrontPaymentIntent>().SingleAsync(x=>x.StoreId==Store&&x.Id==paymentIntentId,ct);
            var state=await db.Set<StorefrontQrProcessing>().SingleAsync(x=>x.StoreId==Store&&x.PaymentIntentId==intent.Id,ct);
            var binding=JsonSerializer.Deserialize<StorefrontBankBinding>(intent.BindingSnapshotJson,Json)??throw new InvalidOperationException("Missing sealed bank binding.");
            var acb=await db.Set<StoreAcbSettings>().AsNoTracking().SingleOrDefaultAsync(x=>x.StoreId==Store,ct);
            var bank=await db.StoreBankAccounts.AsNoTracking().SingleOrDefaultAsync(x=>x.StoreId==Store&&x.Id==binding.BankAccountId,ct);
            if(acb is null||bank is null||StorefrontCheckoutService.ProviderFingerprint(acb)!=binding.ProviderConfigFingerprint||
                GlobalBankReceiptLedger.AccountIdentity(bank.BankCode,bank.AccountNumber)!=binding.AccountIdentity)
            {await MarkReview(intent,state,"BANK_BINDING_CHANGED",ct);return;}
            var account=GlobalBankReceiptLedger.AccountSnapshot(bank);
            state.LastAttemptAtUtc=Now;state.NextAttemptAtUtc=Now.AddSeconds(20);state.Attempts++;
            await db.SaveChangesAsync(ct);
            var acceptingPayment=await db.StorefrontOrders.AsNoTracking().AnyAsync(x=>x.StoreId==Store&&x.Id==intent.StorefrontOrderId&&x.Phase==StorefrontOrderPhase.AwaitingPayment,ct);
            if(acceptingPayment&&state.State==(int)StorefrontQrState.Queued&&intent.ExpiresAtUtc>Now)
            {
                state.State=(int)StorefrontQrState.Creating;await db.SaveChangesAsync(ct);
                await Initiate(intent,state,binding,acb,ct);
            }
            var complete=await Retrieve(intent,state,binding,acb,account,ct);
            if(complete&&intent.ExpiresAtUtc<=Now)await TryExpireUnpaid(intent,state,binding,acb,ct);
            var queued=await db.Set<StorefrontOutboxMessage>().Where(x=>x.StoreId==Store&&x.StorefrontOrderId==intent.StorefrontOrderId&&x.Kind=="QrIntentCreated"&&x.ProcessedAtUtc==null).ToListAsync(ct);
            foreach(var message in queued){message.Attempts++;message.ProcessedAtUtc=Now;}
            state.NextAttemptAtUtc=Now.AddSeconds(state.State==(int)StorefrontQrState.Pending?20:300);
            await db.SaveChangesAsync(ct);
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch(Exception error) when(error is AcbTransportException or HttpRequestException or JsonException or InvalidOperationException or DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var state=await db.Set<StorefrontQrProcessing>().SingleOrDefaultAsync(x=>x.StoreId==Store&&x.PaymentIntentId==paymentIntentId,ct);
            if(state is not null)
            {
                if(error is AcbTransportException {ResponseEvidence:not null} transport)
                {
                    var hash=GlobalBankReceiptLedger.Hash(transport.ResponseEvidence);
                    if(!await db.Set<StorefrontBankObservation>().AnyAsync(x=>x.StoreId==Store&&x.PaymentIntentId==paymentIntentId&&x.Fingerprint==hash,ct))
                        db.Add(new StorefrontBankObservation{StoreId=Store,PaymentIntentId=paymentIntentId,Kind="TransportFailure",Fingerprint=hash,
                            ProtectedEvidence=protocol.Protect(Store,transport.ResponseEvidence),ObservedAtUtc=Now});
                }
                state.NextAttemptAtUtc=Now.AddSeconds(Math.Min(300,Math.Pow(2,Math.Min(state.Attempts+1,8))));
                // Uncertain transport is retried under the SAME persisted reference; it is never a new intent.
                state.ReviewCode??="RETRY_BANK_RESULT_UNKNOWN";await db.SaveChangesAsync(ct);
            }
        }
        finally
        {
            db.ChangeTracker.Clear();
            try{await db.Database.ExecuteSqlInterpolatedAsync($"EXEC sys.sp_releaseapplock @Resource={resource},@LockOwner='Session';",CancellationToken.None);}
            finally{await db.Database.CloseConnectionAsync();}
        }
    }
    private async Task Observe(StorefrontPaymentIntent intent,string kind,JsonElement response,CancellationToken ct,StorefrontQrProcessing? state=null,string? reviewCode=null)
    {
        var raw=response.GetRawText();var hash=GlobalBankReceiptLedger.Hash(raw);
        var rawBytes=Encoding.UTF8.GetByteCount(raw);
        var evidence=rawBytes<=4194304?raw:JsonSerializer.Serialize(new{Sha256=hash,Bytes=rawBytes,SizeExceeded=true});
        if(!await db.Set<StorefrontBankObservation>().AnyAsync(x=>x.StoreId==Store&&x.PaymentIntentId==intent.Id&&x.Fingerprint==hash,ct))
            db.Add(new StorefrontBankObservation{StoreId=Store,PaymentIntentId=intent.Id,Kind=kind,Fingerprint=hash,ProtectedEvidence=protocol.Protect(Store,evidence),ObservedAtUtc=Now});
        if(rawBytes>4194304)reviewCode="EVIDENCE_SIZE_LIMIT";
        if(reviewCode is not null&&state is not null)
        {
            state.State=(int)StorefrontQrState.Review;state.ReviewCode=state.ReviewCode is null||state.ReviewCode.StartsWith("RETRY_",StringComparison.Ordinal)?reviewCode:state.ReviewCode;
            var order=await db.StorefrontOrders.SingleAsync(x=>x.StoreId==Store&&x.Id==intent.StorefrontOrderId,ct);
            if(order.PaymentState!=StorefrontPaymentState.Review){order.PaymentState=StorefrontPaymentState.Review;order.Revision++;}
        }
        // Observation and a known batch anomaly are one atomic SaveChanges transaction.
        await db.SaveChangesAsync(ct);
    }
    private async Task Initiate(StorefrontPaymentIntent intent,StorefrontQrProcessing state,StorefrontBankBinding binding,StoreAcbSettings acb,CancellationToken ct)
    {
        JsonElement response;
        try
        {
            response=await protocol.CallAsync(acb,HttpMethod.Post,acb.QrEndpoint,new{traceNumber=state.TraceNumber,merchantId=binding.MerchantId,
                terminalId=binding.BankTerminalCode,userId=binding.BankUserCode,orderId=intent.ProviderReference,
                virtualAccountPrefix=binding.VirtualAccountPrefix,beneficiaryName=binding.BeneficiaryName,amount=checked((int)intent.AmountDue),description=intent.ProviderReference},ct);
        }
        catch(AcbTransportException error) when(error.BusinessRequestNotSent)
        {
            // Authentication failed before the business request. This is the only proven-safe
            // return to Queued; ambiguous sends retain Creating and require retrieval first.
            if(state.State==(int)StorefrontQrState.Creating&&state.ProviderTraceNumber.Length==0&&state.VirtualAccount.Length==0)
            {state.State=(int)StorefrontQrState.Queued;await db.SaveChangesAsync(ct);}
            throw;
        }
        await Observe(intent,"Initiate",response,ct);
        var body=Path(response,"responseBody");var trace=Text(body,"traceNumber");var account=Text(body,"virtualAccount");
        var png=DecodePng(Text(body,"qrDataUrl"));
        if(trace.Length is <1 or >100||account.Length is <1 or >100||png is null){await MarkReview(intent,state,"INVALID_QR_RESPONSE",ct);return;}
        if(state.ProviderTraceNumber.Length>0&&state.ProviderTraceNumber!=trace||state.VirtualAccount.Length>0&&state.VirtualAccount!=account)
        {await MarkReview(intent,state,"QR_IDENTITY_CHANGED",ct);return;}
        state.ProviderTraceNumber=trace;state.VirtualAccount=account;state.QrPng=png;
        if(state.State!=(int)StorefrontQrState.Review)state.State=(int)StorefrontQrState.Pending;
        await db.SaveChangesAsync(ct);
    }
    internal static byte[]? DecodePng(string value)
    {
        const string prefix="data:image/png;base64,";
        if(!value.StartsWith(prefix,StringComparison.Ordinal)||value.Length>350000)return null;
        try
        {
            var bytes=Convert.FromBase64String(value[prefix.Length..]);
            if(bytes.Length is <24 or >262144||!bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})||
                !bytes.AsSpan(12,4).SequenceEqual(new byte[]{73,72,68,82}))return null;
            var width=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16,4));var height=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20,4));
            return width is >0 and <=2048&&height is >0 and <=2048?bytes:null;
        }
        catch(FormatException){return null;}
    }
    private async Task<bool> Retrieve(StorefrontPaymentIntent intent,StorefrontQrProcessing state,StorefrontBankBinding binding,StoreAcbSettings acb,BankVerifiedAccount account,CancellationToken ct)
    {
        var created=DateTime.SpecifyKind(intent.CreatedAtUtc,DateTimeKind.Utc).AddHours(7).Date;var paid=false;var recoverInit=false;
        var allocations=new Dictionary<int,bool>();
        var allSeen=new HashSet<string>(StringComparer.Ordinal);
        var currentFrom=created.AddDays(-1)>Now.AddHours(7).Date.AddDays(-29)?created.AddDays(-1):Now.AddHours(7).Date.AddDays(-29);
        var cursor=state.NextScanBankDate??created.AddDays(-1);
        var windows=new List<(DateTime From,DateTime To,bool History,bool Requested)>{(currentFrom,Now.AddHours(7).Date.AddDays(1),false,false)};
        if(cursor<currentFrom)windows.Add((cursor,cursor.AddDays(30)<currentFrom?cursor.AddDays(30):currentFrom.AddDays(-1),true,false));
        if(state.RequestedBankDate is DateTime requested)windows.Add((requested.AddDays(-1),requested.AddDays(1),false,true));
        var scanAttempt=await StorefrontBankScanEvidence.StartAsync(db,intent,binding,
            windows.Select(w=>new StorefrontBankScanRange(w.From,w.To)).ToList(),clock,ct);
        var completedWindows=new List<StorefrontBankScanWindow>();
        var nextHistory=state.NextScanBankDate;var nextRequested=state.RequestedBankDate;
        foreach(var window in windows)
        {
        var page=0;var totalPages=1;var seen=new HashSet<string>(StringComparer.Ordinal);
        int? declaredPages=null;var pageFingerprints=new List<string>();
        do
        {
            var trace=state.ProviderTraceNumber.Length>0?state.ProviderTraceNumber:state.TraceNumber;
            var url=acb.ApiBaseUrl.TrimEnd('/')+"/acb/open/payments/qr-payment/v1/retrieve"+
                $"?fromDate={window.From:yyyy-MM-dd}&toDate={window.To:yyyy-MM-dd}&orderId={Uri.EscapeDataString(intent.ProviderReference)}&traceNumber={Uri.EscapeDataString(trace)}&page={page}&size=50";
            var response=await protocol.CallAsync(acb,HttpMethod.Get,url,null,ct);
            var priorNumbers=await db.Set<BankVerifiedReceipt>().AsNoTracking().Where(x=>x.OriginStoreId==Store&&x.ProviderReference==intent.ProviderReference).Select(x=>x.ExternalTransactionId).Take(1001).ToListAsync(ct);
            var returnedOrders=Items(Path(response,"responseBody","orders")).ToList();
            var returnedTransactions=returnedOrders.SelectMany(value=>Items(Path(value,"transactionDetail"))).ToList();
            var paidNumbers=returnedTransactions.Where(value=>IsPaid(Text(value,"transactionStatus"))).Select(value=>Text(value,"transactionNumber")).ToList();
            string? batchReview=priorNumbers.Concat(paidNumbers).Distinct(StringComparer.Ordinal).Count()>1?"MULTIPLE_BANK_RECEIPTS":null;
            if(returnedTransactions.Any(value=>!IsPaid(Text(value,"transactionStatus"))&&priorNumbers.Contains(Text(value,"transactionNumber"),StringComparer.Ordinal)))batchReview="VERIFIED_RECEIPT_CORRECTED";
            if(returnedOrders.Any(value=>ResponseBindingMismatch(value,intent,state,binding)))batchReview="RETRIEVE_BINDING_MISMATCH";
            await Observe(intent,"Retrieve",response,ct,state,batchReview);
            var body=Path(response,"responseBody");var orders=Path(body,"orders");var pagination=Path(body,"pagination","totalPages");
            if(orders.ValueKind!=JsonValueKind.Array||orders.GetArrayLength()>100||pagination.ValueKind!=JsonValueKind.Number||!pagination.TryGetInt32(out totalPages)||totalPages is <0 or >100||totalPages==0&&orders.GetArrayLength()!=0)
            {await MarkReview(intent,state,"INVALID_RETRIEVE_PAGE",ct);return false;}
            if(declaredPages.HasValue&&declaredPages.Value!=totalPages){await MarkReview(intent,state,"RETRIEVE_PAGINATION_CHANGED",ct);return false;}
            declaredPages=totalPages;pageFingerprints.Add(GlobalBankReceiptLedger.Hash(response.GetRawText()));
            foreach(var order in Items(orders))
            {
                var reportedReference=Text(order,"orderId");var matchesRequested=reportedReference==intent.ProviderReference;
                var bankTrace=Text(order,"traceNumber");var bankAmount=Path(order,"amount");
                var attributes=Path(order,"transactionEntityAttribute");
                var beneficiary=Text(order,"beneficiaryAccountNumber");if(beneficiary.Length==0)beneficiary=Text(attributes,"beneficiaryAccountNumber");
                var contradictoryAccount=beneficiary.Length>0&&GlobalBankReceiptLedger.AccountIdentity(binding.BankCode,beneficiary)!=binding.AccountIdentity;
                var mismatch=ResponseBindingMismatch(order,intent,state,binding);
                if(mismatch)await MarkReview(intent,state,"RETRIEVE_BINDING_MISMATCH",ct);
                if(matchesRequested&&state.ProviderTraceNumber.Length==0&&bankTrace.Length is >0 and <=100){state.ProviderTraceNumber=bankTrace;await db.SaveChangesAsync(ct);}
                var transactions=Path(order,"transactionDetail");
                if(transactions.ValueKind!=JsonValueKind.Array||transactions.GetArrayLength()>1000){await MarkReview(intent,state,"INVALID_TRANSACTION_LIST",ct);continue;}
                foreach(var entry in Items(transactions))
                {
                    var number=Text(entry,"transactionNumber");var amountValue=Path(entry,"transactionAmount");
                    if(number.Length>0){seen.Add(number);if(matchesRequested)allSeen.Add(number);}
                    if(!IsPaid(Text(entry,"transactionStatus")))
                    {
                        var prior=number.Length==0?null:await db.Set<BankVerifiedReceipt>().AsNoTracking().SingleOrDefaultAsync(x=>x.OriginStoreId==Store&&x.ProviderReference==intent.ProviderReference&&x.ExternalTransactionId==number,ct);
                        if(prior is not null)
                        {
                            await new GlobalBankReceiptLedger(db,protocol,clock).RecordVerifiedAsync(new(account,intent.ProviderReference,BankPaymentChannel.Storefront,number,
                                prior.Amount,Text(entry,"transactionStatus"),Text(entry,"origPostDate"),entry.GetRawText()),ct);
                            await MarkReview(intent,state,"VERIFIED_RECEIPT_CORRECTED",ct);
                        }
                        continue;
                    }
                    paid=true;
                    if(string.IsNullOrWhiteSpace(number)||number.Length>100||amountValue.ValueKind!=JsonValueKind.Number||!amountValue.TryGetDecimal(out var amount)||
                        amount<=0||amount>=StorefrontValues.MoneyLimit||decimal.Truncate(amount)!=amount)
                    {await MarkReview(intent,state,"INVALID_VERIFIED_TRANSACTION",ct);continue;}
                    if(contradictoryAccount)
                    {
                        var rawTime=Text(entry,"origPostDate");var parsed=BankEvidenceTime.Parse(rawTime);
                        var fingerprint=GlobalBankReceiptLedger.Hash(entry.GetRawText()+"|"+beneficiary);
                        if(!await db.Set<StorefrontUnattributedBankReceipt>().AnyAsync(x=>x.StoreId==Store&&x.PaymentIntentId==intent.Id&&x.Fingerprint==fingerprint,ct))
                        {
                            db.Add(new StorefrontUnattributedBankReceipt {StoreId=Store,PaymentIntentId=intent.Id,Fingerprint=fingerprint,ExternalTransactionId=number,Amount=amount,
                                RequestedAccountIdentity=binding.AccountIdentity,ReportedAccountIdentity=GlobalBankReceiptLedger.AccountIdentity(binding.BankCode,beneficiary),RawBankTime=rawTime[..Math.Min(100,rawTime.Length)],
                                TimePrecision=parsed.Precision,BankBusinessDate=parsed.BusinessDate,OccurredAtUtc=parsed.OccurredAtUtc,ObservedAtUtc=Now,
                                ProtectedEvidence=protocol.Protect(Store,JsonSerializer.Serialize(new{Requested=binding.AccountIdentity,Reported=beneficiary,Transaction=entry},Json))});
                            await db.SaveChangesAsync(ct);
                        }
                        continue; // No verified cash inflow for either account and no automatic rerouting.
                    }
                    var receipt=await new GlobalBankReceiptLedger(db,protocol,clock).RecordVerifiedAsync(new(account,reportedReference,matchesRequested?BankPaymentChannel.Storefront:BankPaymentChannel.Unknown,
                        number,amount,Text(entry,"transactionStatus"),Text(entry,"origPostDate"),entry.GetRawText(),CrossProtocolMappingVerified:binding.NotificationIdentityMappingVerified,
                        RefundReviewProtocolVersion:matchesRequested?1:0,RefundReviewObservationFingerprint:matchesRequested?GlobalBankReceiptLedger.Hash(response.GetRawText()):null),ct);
                    if(matchesRequested)allocations[receipt.Id]=allocations.GetValueOrDefault(receipt.Id)||mismatch;
                }
                if(!paid&&state.State==(int)StorefrontQrState.Creating&&Text(order,"status")=="INIT"&&intent.ExpiresAtUtc>Now)
                    recoverInit=true;
            }
            page++;
        }while(page<totalPages);
        var missing=await db.Set<BankVerifiedReceipt>().AsNoTracking().AnyAsync(x=>x.OriginStoreId==Store&&x.ProviderReference==intent.ProviderReference&&
            x.BankBusinessDate>=window.From&&x.BankBusinessDate<=window.To&&!seen.Contains(x.ExternalTransactionId),ct);
        if(missing)await MarkReview(intent,state,"PREVIOUS_RECEIPT_NOT_CONFIRMED",ct);
        completedWindows.Add(new(new(window.From,window.To),totalPages,pageFingerprints));
        if(window.History)nextHistory=window.To.AddDays(1);
        if(window.Requested)nextRequested=null;
        await db.SaveChangesAsync(ct);
        }
        // Classify the complete bounded retrieval before committing even the first allocation.
        // A crash between two known paid entries must never leave a false payment-success event.
        if(await db.Set<BankVerifiedReceipt>().AsNoTracking().AnyAsync(x=>x.OriginStoreId==Store&&x.ProviderReference==intent.ProviderReference&&
            x.TimePrecision==BankTimePrecision.Unknown&&!allSeen.Contains(x.ExternalTransactionId),ct))
            await MarkReview(intent,state,"PREVIOUS_RECEIPT_NOT_CONFIRMED",ct);
        if(await db.Set<BankVerifiedReceipt>().CountAsync(x=>x.OriginStoreId==Store&&x.ProviderReference==intent.ProviderReference,ct)>1)
            await MarkReview(intent,state,"MULTIPLE_BANK_RECEIPTS",ct);
        // Advance resumable history only together with complete evidence. An early
        // page/window failure must not skip a range during the genuine retry.
        state.NextScanBankDate=nextHistory;state.RequestedBankDate=nextRequested;
        await StorefrontBankScanEvidence.CompleteAsync(db,protocol,intent,scanAttempt,completedWindows,clock,ct);
        foreach(var allocation in allocations)
        {await Allocate(intent.Id,allocation.Key,allocation.Value,ct);await db.Entry(state).ReloadAsync(ct);}
        if(!paid&&recoverInit&&state.State==(int)StorefrontQrState.Creating&&intent.ExpiresAtUtc>Now&&
            await db.StorefrontOrders.AsNoTracking().AnyAsync(x=>x.StoreId==Store&&x.Id==intent.StorefrontOrderId&&x.Phase==StorefrontOrderPhase.AwaitingPayment,ct))
            // ACB documents idempotent INIT for the exact same orderId+amount; never change either value.
            await Initiate(intent,state,binding,acb,ct);
        return true;
    }
    private async Task MarkReview(StorefrontPaymentIntent intent,StorefrontQrProcessing state,string code,CancellationToken ct,JsonElement? callbackEvidence=null)
    {
        // Reuse the caller's receipt/order locks and transaction during allocation. Other
        // review paths need one transaction for both stages, never an intermediate commit.
        await using var ownedTransaction=db.Database.CurrentTransaction is null?await db.Database.BeginTransactionAsync(ct):null;
        var order=await db.StorefrontOrders.SingleAsync(x=>x.StoreId==Store&&x.Id==intent.StorefrontOrderId,ct);
        var orderEntry=db.Entry(order);
        if(orderEntry.State!=EntityState.Unchanged)
            throw new InvalidOperationException("Bank review cannot refresh an order with unsaved caller changes.");
        await AppendRefundRestriction(intent,code,callbackEvidence,ct);
        state.State=(int)StorefrontQrState.Review;state.ReviewCode=state.ReviewCode is null||state.ReviewCode.StartsWith("RETRY_",StringComparison.Ordinal)?code:state.ReviewCode;
        if(code is "PREVIOUS_RECEIPT_NOT_CONFIRMED" or "BANK_CORRECTION_NOTIFICATION" or "VERIFIED_RECEIPT_CORRECTED")
        {
            var receipts=await db.Set<BankVerifiedReceipt>().Where(x=>x.OriginStoreId==Store&&x.ProviderReference==intent.ProviderReference).ToListAsync(ct);
            foreach(var receipt in receipts){receipt.NeedsReview=true;receipt.ReviewCode="BANK_CONFIRMATION_CORRECTED";}
        }
        // Receipt proof triggers can synchronize this order's Review/revision/rowversion.
        // Flush the latches first, then re-read and decide from that in-transaction proof.
        // Keep optimistic concurrency; never overwrite a stale OriginalValue.
        await db.SaveChangesAsync(ct);
        await orderEntry.ReloadAsync(ct);
        if(order.PaymentState!=StorefrontPaymentState.Review){order.PaymentState=StorefrontPaymentState.Review;order.Revision++;}
        await db.SaveChangesAsync(ct);
        if(ownedTransaction is not null)await ownedTransaction.CommitAsync(ct);
    }
    private static bool ResponseBindingMismatch(JsonElement value,StorefrontPaymentIntent intent,StorefrontQrProcessing state,StorefrontBankBinding binding)
    {
        static bool Different(JsonElement item,string name,string expected){var actual=Text(item,name);return actual.Length>0&&actual!=expected;}
        var attrs=Path(value,"transactionEntityAttribute");var beneficiary=Text(value,"beneficiaryAccountNumber");if(beneficiary.Length==0)beneficiary=Text(attrs,"beneficiaryAccountNumber");
        var amount=Path(value,"amount");var expectedTrace=state.ProviderTraceNumber.Length>0?state.ProviderTraceNumber:state.TraceNumber;
        return Text(value,"orderId")!=intent.ProviderReference||Text(value,"traceNumber")!=expectedTrace||amount.ValueKind!=JsonValueKind.Number||!amount.TryGetDecimal(out var parsed)||parsed!=intent.AmountDue||
            beneficiary.Length>0&&GlobalBankReceiptLedger.AccountIdentity(binding.BankCode,beneficiary)!=binding.AccountIdentity||
            Different(value,"merchantId",binding.MerchantId)||Different(value,"terminalId",binding.BankTerminalCode)||Different(value,"userId",binding.BankUserCode)||
            Different(attrs,"custom1",binding.MerchantId)||Different(attrs,"custom2",binding.BankTerminalCode)||Different(attrs,"custom3",binding.BankUserCode)||
            state.VirtualAccount.Length>0&&(Different(value,"virtualAccount",state.VirtualAccount)||Different(attrs,"virtualAccount",state.VirtualAccount));
    }
}
