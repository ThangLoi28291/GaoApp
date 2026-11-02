using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GaoApp.Infrastructure.Services.Banking;

// Called only after authenticated provider retrieval. Notification content is never financial proof.
public sealed class GlobalBankReceiptLedger(AppDbContext db, AcbProtocolCore protocol, TimeProvider? time = null)
{
    private int Store => db.CurrentStoreId is > 0 ? db.CurrentStoreId.Value : throw new ForbiddenAppException("Thiếu phạm vi xử lý ngân hàng.");
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string AccountIdentity(string bankCode, string account) => BankPhysicalAccountIdentity.Create(bankCode, account);
    public static BankVerifiedAccount AccountSnapshot(StoreBankAccount account) => new(account.Id,account.StoreId,account.BankCode,account.AccountNumber,Convert.ToBase64String(account.RowVersion??[]));
    public static bool BlocksPosAllocation(BankVerifiedReceipt receipt)=>receipt.AssetConfidence!=BankReceiptAssetConfidence.CertifiedDistinct||receipt.NeedsReview&&receipt.ReviewCode!="BANK_TIME_UNKNOWN";
    public static async Task LockAsync(AppDbContext db, string resource, CancellationToken ct)
    {
        if (!db.Database.IsSqlServer()) return; // Non-SQL providers are used only by isolated legacy transport fixtures.
        await using var command = db.Database.GetDbConnection().CreateCommand(); command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000; IF @r<0 THROW 51140,'Bank evidence is busy; retry the same command.',1;";
        var parameter=command.CreateParameter(); parameter.ParameterName="@resource"; parameter.Value=resource; command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(ct);
    }
    public async Task RegisterPosAsync(int qrRequestId, string reference, CancellationToken ct,bool providerReference=true)
    {
        if (reference.Length<1||reference.Length>(providerReference?13:80)) throw new ValidationAppException("Mã QR vượt giới hạn nguồn thanh toán.");
        var existing = await db.Set<BankPaymentReference>().SingleOrDefaultAsync(x => x.Provider == "ACB" && x.Reference == reference, ct);
        if (existing is not null)
        {
            if(existing.Channel != BankPaymentChannel.Pos || existing.OriginStoreId != Store || existing.PosQrRequestId != qrRequestId)
                throw new ConflictAppException("Mã ngân hàng đã thuộc một yêu cầu khác.");
            return;
        }
        var qr=await db.PosPaymentQrRequests.AsNoTracking().SingleAsync(x=>x.StoreId==Store&&x.Id==qrRequestId,ct);
        var bank=await db.StoreBankAccounts.AsNoTracking().SingleAsync(x=>x.StoreId==Store&&x.Id==qr.BankAccountId,ct);
        db.Add(new BankPaymentReference { OriginStoreId=Store, Reference=reference, Channel=BankPaymentChannel.Pos, PosQrRequestId=qrRequestId,AccountIdentity=AccountIdentity(bank.BankCode,bank.AccountNumber) });
        await db.SaveChangesAsync(ct);
    }
    public async Task<BankReceiptRecord> RecordVerifiedAsync(BankVerifiedInput input, CancellationToken ct = default,
        Func<CancellationToken, Task<bool>>? nativeAmbiguityVerifier = null)
    {
        if (db.Database.CurrentTransaction is not null) throw new InvalidOperationException("Commit bank inbox independently before allocating money.");
        if (input.ExternalTransactionId.Length is < 1 or > 100 || input.ExternalTransactionId != input.ExternalTransactionId.Trim() ||
            input.ExternalTransactionId.Any(char.IsControl) || input.ProviderReference.Length > 300 || input.Status.Length > 50)
            throw new ValidationAppException("Bằng chứng ngân hàng có định danh không hợp lệ.");
        StorefrontValues.Money(input.Amount);
        if(input.Amount<=0||decimal.Truncate(input.Amount)!=input.Amount)throw new ValidationAppException("Số tiền ngân hàng phải là số nguyên dương trong giới hạn.");
        if(input.Account.StoreId!=Store) throw new ForbiddenAppException("Bằng chứng tài khoản không thuộc phạm vi đã xác thực.");
        var bank=await db.StoreBankAccounts.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId==Store && x.Id==input.Account.Id,ct)
            ?? throw new ValidationAppException("Tài khoản bằng chứng không thuộc cửa hàng được xác thực.");
        var account=AccountIdentity(input.Account.BankCode,input.Account.AccountNumber);
        var fingerprint=Hash(JsonSerializer.Serialize(new {input.ProviderReference,input.Channel,input.IdentityKind,Amount=input.Amount.ToString("G29",System.Globalization.CultureInfo.InvariantCulture),input.Status,input.RawBankTime}));
        var parsed=BankEvidenceTime.Parse(input.RawBankTime);
        var observed=(time??TimeProvider.System).GetUtcNow().UtcDateTime;
        var oversized=Encoding.UTF8.GetByteCount(input.RawEvidence)>262144;
        var refundReviewCoverage = await VerifyRefundReviewCoverage(input, ct);
        var accountChangedAtIntake = false;
        // Retain the amount/identity even if a provider sends excessive raw content; never log response bodies.
        string ProtectEvidence(BankReceiptAssetConfidence confidence,bool? legacyClaim=null,bool? identityAmbiguous=null)=>protocol.Protect(Store,JsonSerializer.Serialize(new {
            OriginalAssetConfidence=confidence,HistoricalClaimOverlap=legacyClaim,CrossProtocolAmbiguity=identityAmbiguous,
            input.RefundReviewProtocolVersion,input.RefundReviewObservationFingerprint,RefundIntakeObservationVerified=refundReviewCoverage,
            AccountChangedAtIntake=accountChangedAtIntake,
            input.Account,input.ProviderReference,ClaimedChannel=input.Channel,input.IdentityKind,input.CrossProtocolMappingVerified,input.UnprovenCrossProtocolExposure,input.NativeCollectionAuthorities,input.RawBankTime,input.Status,Amount=input.Amount.ToString("G29",System.Globalization.CultureInfo.InvariantCulture),
            Raw=oversized?null:input.RawEvidence,RawSha256=Hash(input.RawEvidence),RawBytes=Encoding.UTF8.GetByteCount(input.RawEvidence),Truncated=oversized}));
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        // Different native identifier kinds still describe one physical account. Serialize their
        // confidence checks so two concurrent observations cannot both become certified inflows.
        await LockAsync(db,"GaoApp:BankAccountEvidence:"+account,ct);
        var nativeAmbiguity = nativeAmbiguityVerifier is not null && await nativeAmbiguityVerifier(ct);
        await LockAsync(db,"GaoApp:BankReceipt:"+Hash("ACB|"+account+"|"+input.ExternalTransactionId),ct);
        // The immutable pre-lookup identity remains the key. Recheck current configuration under a row lock through commit.
        bank=db.Database.IsSqlServer()
            ?await db.StoreBankAccounts.FromSqlInterpolated($"SELECT * FROM [dbo].[StoreBankAccounts] WITH(HOLDLOCK,ROWLOCK) WHERE [Id]={input.Account.Id} AND [StoreId]={Store}").IgnoreQueryFilters().AsNoTracking().SingleAsync(ct)
            :await db.StoreBankAccounts.IgnoreQueryFilters().AsNoTracking().SingleAsync(x=>x.StoreId==Store&&x.Id==input.Account.Id,ct);
        var accountChanged=bank.IsDeleted||!bank.IsActive||account!=AccountIdentity(bank.BankCode,bank.AccountNumber)||input.Account.Version!=Convert.ToBase64String(bank.RowVersion??[]);
        accountChangedAtIntake=accountChanged;
        var row=await db.Set<BankVerifiedReceipt>().SingleOrDefaultAsync(x=>x.Provider=="ACB"&&x.AccountIdentity==account&&x.ExternalTransactionId==input.ExternalTransactionId,ct);
        // PHONE: is our own namespace marker, not a guessed provider alias. Only a verified literal-ID mapping permits crossing kinds.
        if(row is null&&(input.IdentityKind==BankExternalIdentityKind.BankNotification||input.CrossProtocolMappingVerified))
        {
            var alias=input.ExternalTransactionId.StartsWith("PHONE:",StringComparison.Ordinal)?input.ExternalTransactionId[6..]:"PHONE:"+input.ExternalTransactionId;
            row=await db.Set<BankVerifiedReceipt>().SingleOrDefaultAsync(x=>x.Provider=="ACB"&&x.AccountIdentity==account&&x.ExternalTransactionId==alias&&x.IdentityKind==BankExternalIdentityKind.BankNotification,ct);
        }
        var duplicate=row is not null; var conflict=false;
        if(row is null)
        {
            var legacyClaim=await LegacyClaimMayOverlap(input.ExternalTransactionId,account,ct);
            var identityAmbiguous=nativeAmbiguity || !input.CrossProtocolMappingVerified&&(input.UnprovenCrossProtocolExposure||
                await db.Set<BankVerifiedReceipt>().AsNoTracking().AnyAsync(x=>x.Provider=="ACB"&&x.AccountIdentity==account&&x.IdentityKind!=input.IdentityKind&&x.AssetConfidence==BankReceiptAssetConfidence.CertifiedDistinct,ct)||
                input.IdentityKind==BankExternalIdentityKind.AcbRetrieve&&await LegacyPhoneMayOverlap(account,ct));
            var registry=await db.Set<BankPaymentReference>().AsNoTracking().SingleOrDefaultAsync(x=>x.Provider=="ACB"&&x.Reference==input.ProviderReference,ct);
            var mapped=registry is not null&&registry.OriginStoreId==Store&&registry.Channel==input.Channel;
            conflict=accountChanged || !mapped || legacyClaim || identityAmbiguous;
            var confidence=identityAmbiguous?BankReceiptAssetConfidence.TransactionIdentityAmbiguous:
                legacyClaim?BankReceiptAssetConfidence.LegacySourceUnproven:BankReceiptAssetConfidence.CertifiedDistinct;
            row=new BankVerifiedReceipt { OriginStoreId=Store, BankAccountId=bank.Id, AccountIdentity=account,
                AccountVersion=input.Account.Version,
                NativeCollectionAuthorityProofJson=input.NativeCollectionAuthorities is {Count:>0 and <=1000}?JsonSerializer.Serialize(input.NativeCollectionAuthorities):null,
                RoutingProvenance=mapped?BankReceiptRoutingProvenance.BoundReference:
                    !accountChanged&&!legacyClaim&&!identityAmbiguous&&!oversized&&parsed.Precision!=BankTimePrecision.Unknown&&parsed.OccurredAtUtc<=observed.AddMinutes(2)
                    ?BankReceiptRoutingProvenance.UnmatchedOnly:BankReceiptRoutingProvenance.Unproven,
                ExternalTransactionId=input.ExternalTransactionId, ProviderReference=input.ProviderReference, Channel=mapped?input.Channel:BankPaymentChannel.Unknown,
                AssetConfidence=confidence,IdentityKind=input.IdentityKind,CrossProtocolMappingVerified=input.CrossProtocolMappingVerified,Amount=input.Amount,Status=input.Status,Fingerprint=fingerprint,ProtectedEvidence=ProtectEvidence(confidence,legacyClaim,identityAmbiguous),RawBankTime=input.RawBankTime[..Math.Min(100,input.RawBankTime.Length)],
                TimePrecision=parsed.Precision,BankBusinessDate=parsed.BusinessDate,OccurredAtUtc=parsed.OccurredAtUtc,VerifiedAtUtc=observed,
                NeedsReview=conflict||oversized||parsed.Precision==BankTimePrecision.Unknown,
                ReviewCode=identityAmbiguous?"TRANSACTION_IDENTITY_AMBIGUOUS":legacyClaim?"LEGACY_SOURCE_UNPROVEN":accountChanged?"BANK_ACCOUNT_CHANGED":!mapped?"UNMATCHED_CHANNEL_BINDING":oversized?"EVIDENCE_SIZE_LIMIT":parsed.Precision==BankTimePrecision.Unknown?"BANK_TIME_UNKNOWN":null };
            db.Add(row); await db.SaveChangesAsync(ct);
        }
        else if(nativeAmbiguity || accountChanged || row.Fingerprint!=fingerprint || row.OriginStoreId!=Store || row.BankAccountId!=bank.Id)
        {
            conflict=true; row.NeedsReview=true; row.ReviewCode=nativeAmbiguity?"NATIVE_SOURCE_AMBIGUITY":"EXTERNAL_ID_CONFLICT";
            if(!await db.Set<BankReceiptConflict>().AnyAsync(x=>x.BankVerifiedReceiptId==row.Id&&x.Fingerprint==fingerprint&&x.AttemptedStoreId==Store,ct))
                db.Add(new BankReceiptConflict {BankVerifiedReceiptId=row.Id,Fingerprint=fingerprint,ProtectedEvidence=ProtectEvidence(row.AssetConfidence),AttemptedReference=input.ProviderReference,AttemptedStoreId=Store,ObservedAmount=input.Amount,ObservedIdentityKind=input.IdentityKind});
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return new(row.Id,conflict||row.AssetConfidence!=BankReceiptAssetConfidence.CertifiedDistinct||(input.Channel==BankPaymentChannel.Pos?BlocksPosAllocation(row):row.NeedsReview),duplicate,row.TimePrecision,row.BankBusinessDate,
            row.OccurredAtUtc.HasValue?DateTime.SpecifyKind(row.OccurredAtUtc.Value,DateTimeKind.Utc):null,DateTime.SpecifyKind(row.VerifiedAtUtc,DateTimeKind.Utc));
    }
    private async Task<bool> VerifyRefundReviewCoverage(BankVerifiedInput input, CancellationToken ct)
    {
        // Additional native refund provenance never changes financial ingestion.
        // An invalid/missing claim leaves the full receipt durable but unqualified
        // for this later workflow; an old duplicate never gains a new initial proof.
        if (input.RefundReviewProtocolVersion != 1 || input.RefundReviewObservationFingerprint is not { Length: 64 } ||
            input.Channel != BankPaymentChannel.Storefront || input.IdentityKind != BankExternalIdentityKind.AcbRetrieve) return false;
        var reference = await db.Set<BankPaymentReference>().AsNoTracking().SingleOrDefaultAsync(x => x.Provider == "ACB" &&
            x.Reference == input.ProviderReference && x.OriginStoreId == Store && x.Channel == BankPaymentChannel.Storefront, ct);
        if (reference?.StorefrontPaymentIntentId is not int intentId) return false;
        var observations = await db.Set<StorefrontBankObservation>().AsNoTracking().Where(x => x.StoreId == Store &&
            x.PaymentIntentId == intentId && x.Kind == "Retrieve" && x.Fingerprint == input.RefundReviewObservationFingerprint).Take(2).ToListAsync(ct);
        if (observations.Count != 1 || observations[0].Fingerprint != input.RefundReviewObservationFingerprint) return false;
        try
        {
            var raw = protocol.Unprotect(Store, observations[0].ProtectedEvidence);
            if (Encoding.UTF8.GetByteCount(raw) > 4194304 || Hash(raw) != observations[0].Fingerprint) return false;
            using var document = JsonDocument.Parse(raw);
            bool Match(JsonElement value)
            {
                if (value.ValueKind == JsonValueKind.Object)
                {
                    if (value.TryGetProperty("transactionNumber", out _) && AcbProtocolCore.Text(value, "transactionNumber") == input.ExternalTransactionId &&
                        value.TryGetProperty("transactionAmount", out var amount) && amount.TryGetDecimal(out var actual) && actual == input.Amount &&
                        AcbProtocolCore.Text(value, "transactionStatus") == input.Status && AcbProtocolCore.Text(value, "origPostDate") == input.RawBankTime &&
                        value.GetRawText() == input.RawEvidence) return true;
                    foreach (var field in value.EnumerateObject()) if (Match(field.Value)) return true;
                }
                else if (value.ValueKind == JsonValueKind.Array)
                    foreach (var item in value.EnumerateArray()) if (Match(item)) return true;
                return false;
            }
            return Match(document.RootElement);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or InvalidOperationException or FormatException) { return false; }
    }
    private async Task<bool> LegacyClaimMayOverlap(string externalId,string identity,CancellationToken ct)
    {
        // Old physical-account rows were mutable. An exact external-ID overlap is not permission to guess their old account.
        // Soft deletion does not undo a committed financial claim; retain transaction/session tombstones too.
        var old=(from transaction in db.Set<AcbPaymentTransaction>().IgnoreQueryFilters().AsNoTracking()
                 join session in db.Set<AcbQrSession>().IgnoreQueryFilters().AsNoTracking() on transaction.SessionId equals session.Id
                 where transaction.BankVerifiedReceiptId==null&&session.PaymentId!=null&&transaction.TransactionNumber==externalId
                 select session.BankBindingSnapshotJson);
        var snapshots=await old.Take(201).ToListAsync(ct);if(snapshots.Count>200)return true;
        foreach(var snapshot in snapshots)
        {
            if(string.IsNullOrWhiteSpace(snapshot))return true;
            try{var account=JsonSerializer.Deserialize<BankVerifiedAccount>(snapshot);if(account is null||AccountIdentity(account.BankCode,account.AccountNumber)==identity)return true;}
            catch(JsonException){return true;}
        }
        var notificationClaims=await (from notification in db.Set<BankNotificationReceipt>().IgnoreQueryFilters().AsNoTracking()
            join device in db.Set<BankNotificationDevice>().IgnoreQueryFilters().AsNoTracking() on notification.DeviceId equals device.Id
            join payment in db.OrderPayments.IgnoreQueryFilters().AsNoTracking() on notification.PaymentId equals (int?)payment.Id
            where notification.TransactionCode==externalId&&payment.BankVerifiedReceiptId==null
            select device.BankAccountFingerprint).Take(201).ToListAsync(ct);
        return notificationClaims.Count>200||notificationClaims.Any(fingerprint=>string.IsNullOrWhiteSpace(fingerprint)||fingerprint==identity);
    }
    private Task<bool> LegacyPhoneMayOverlap(string identity,CancellationToken ct)=>
        (from notification in db.Set<BankNotificationReceipt>().IgnoreQueryFilters().AsNoTracking()
         join device in db.Set<BankNotificationDevice>().IgnoreQueryFilters().AsNoTracking() on notification.DeviceId equals device.Id
         join payment in db.OrderPayments.IgnoreQueryFilters().AsNoTracking() on notification.PaymentId equals (int?)payment.Id
         where payment.BankVerifiedReceiptId==null&&device.BankAccountFingerprint==identity select notification.Id).AnyAsync(ct);
}
