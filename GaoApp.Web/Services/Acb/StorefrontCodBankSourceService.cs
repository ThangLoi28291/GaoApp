using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Services.Banking;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.Acb;

// Admin host only: its protector owns the actual ACB credentials and evidence purpose.
public sealed class StorefrontCodBankSourceService(AppDbContext db, ICurrentUser user, ICurrentStorePermissionService permissions,
    AcbProtocolCore protocol, TimeProvider clock, IStorefrontBankRegistration? registration = null) : IStorefrontCodBankSourceService
{
    private int Store => db.CurrentStoreId is > 0 ? db.CurrentStoreId.Value : throw new ForbiddenAppException("Chưa chọn cửa hàng.");
    private async Task Guard(CancellationToken ct)
    {
        if (!user.IsAuthenticated || user.UserId is not > 0 || !await permissions.HasPermissionAsync(Store, user.UserId.Value, StorefrontPermissions.Reconcile, ct) ||
            !await db.UserInStores.AnyAsync(x => x.StoreId == Store && x.UserId == user.UserId && x.IsActive && x.User.IsActive, ct))
            throw new ForbiddenAppException("Không có quyền đối soát nguồn tiền COD.");
    }
    public async Task<IReadOnlyList<StorefrontCodBankCandidate>> ListAsync(CancellationToken ct = default)
    {
        await Guard(ct);
        var rows = await db.Set<BankVerifiedReceipt>().AsNoTracking().Where(x => x.OriginStoreId == Store && x.Channel == BankPaymentChannel.Unknown &&
            (x.Status == "PAID" || x.Status == "SUCCESS" || x.Status == "COMPLETED" || x.Status == "APPROVED") &&
            x.AssetConfidence == BankReceiptAssetConfidence.CertifiedDistinct && x.RoutingProvenance == BankReceiptRoutingProvenance.UnmatchedOnly && x.NativeCollectionAuthorityProofJson != null && x.NeedsReview && x.ReviewCode == "UNMATCHED_CHANNEL_BINDING" && x.OccurredAtUtc != null &&
            !db.Set<BankReceiptConflict>().Any(y => y.BankVerifiedReceiptId == x.Id) && !db.Set<BankReceiptConsumption>().Any(y => y.BankVerifiedReceiptId == x.Id) &&
            !db.Set<StorefrontCodBankRouting>().Any(y => y.GlobalBankReceiptId == x.Id) && !db.Set<BankPaymentReference>().Any(y => y.Provider == x.Provider && y.Reference == x.ProviderReference))
            .OrderByDescending(x => x.Id).Take(100).ToListAsync(ct);
        return rows.Select(x => new StorefrontCodBankCandidate(x.Id, x.ExternalTransactionId,
            x.Amount.ToString("#,0", CultureInfo.GetCultureInfo("vi-VN")) + " đ", x.TimePrecision == BankTimePrecision.BankDay
                ? x.BankBusinessDate!.Value.ToString("dd/MM/yyyy") + " (ngày ngân hàng)" : x.OccurredAtUtc!.Value.AddHours(7).ToString("dd/MM/yyyy HH:mm:ss") + " (giờ Việt Nam)")).ToList();
    }
    public async Task<int> ImportAuthenticatedNotificationAsync(int notificationReceiptId, CancellationToken ct = default)
    {
        await Guard(ct);
        if (db.Database.CurrentTransaction is not null) throw new ConflictAppException("Nguồn tiền cần được lưu độc lập trước đối soát đơn.");
        var attestation = await db.Set<StorefrontNotificationAttestation>().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.BankNotificationReceiptId == notificationReceiptId, ct)
            ?? throw new ConflictAppException("Thông báo chưa có bằng chứng tiếp nhận xác thực nguyên thủy. Không chứng nhận lại dữ liệu cũ.");
        var receipt = await db.Set<BankNotificationReceipt>().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == notificationReceiptId, ct)
            ?? throw new NotFoundAppException("Không tìm thấy thông báo gốc.");
        var parsed = BankNotificationParser.Parse(receipt.Content) ?? throw new ConflictAppException("Thông báo không đủ bằng chứng toàn vẹn.");
        if (attestation.SourcePackage != "mobile.acb.com.vn" || parsed.Amount != receipt.Amount || parsed.TransactionCode != receipt.TransactionCode ||
            parsed.OrderId != receipt.OrderId || parsed.QrCode != receipt.QrCode || parsed.BankOccurredAtUtc != attestation.BankOccurredAtUtc || parsed.TransactionCode != attestation.NativeTransactionId ||
            receipt.PayloadHash != BankNotificationParser.Hash(BankNotificationParser.Fold(receipt.Content)) || receipt.PayloadHash != attestation.PayloadHash ||
            receipt.EvidenceKey != attestation.EvidenceKey || receipt.DeviceId != attestation.DeviceId || receipt.BankAccountId != attestation.BankAccountId ||
            parsed.OrderId.HasValue || parsed.QrCode is not null || receipt.PaymentId.HasValue || receipt.AppliedWaitId.HasValue)
            throw new ConflictAppException("Thông báo đã đổi, gắn POS/QR hoặc được sử dụng; không thể làm nguồn COD mới.");
        var device = await db.Set<BankNotificationDevice>().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == attestation.DeviceId && x.Enabled, ct);
        var bank = await db.StoreBankAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == attestation.BankAccountId && x.IsActive, ct);
        if (device is null || bank is null || device.BankAccountId != bank.Id || device.BankAccountFingerprint != attestation.AccountIdentity ||
            GlobalBankReceiptLedger.AccountIdentity(bank.BankCode, bank.AccountNumber) != attestation.AccountIdentity)
            throw new ConflictAppException("Nguồn thiết bị/tài khoản không còn khớp chứng cứ tiếp nhận.");
        var original = JsonSerializer.Deserialize<BankVerifiedAccount>(attestation.AccountSnapshotJson) ?? throw new ConflictAppException("Thiếu nguồn tài khoản nguyên thủy.");
        if (original.Id != attestation.BankAccountId || original.StoreId != Store || original.Version != attestation.AccountVersion ||
            GlobalBankReceiptLedger.AccountIdentity(original.BankCode, original.AccountNumber) != attestation.AccountIdentity)
            throw new ConflictAppException("Chứng cứ tài khoản nguyên thủy không khớp thông báo.");
        var proof = registration?.Get(Store); var mapped = proof?.NotificationIdentityMappingVerified == true && proof.AccountIdentity == attestation.AccountIdentity;
        var exposure = attestation.HadManualAmbiguity || !mapped && await db.Set<BankPaymentReference>().AnyAsync(x => x.AccountIdentity == attestation.AccountIdentity && x.Channel == BankPaymentChannel.Storefront && !x.NotificationIdentityMappingVerified, ct);
        // MGD remains scoped to its actual bank day. Android posted time never supplies a bank date.
        var native = parsed.TransactionCode.StartsWith("MGD:", StringComparison.Ordinal)
            ? parsed.TransactionCode + ":" + (parsed.BankOccurredAtUtc?.AddHours(7).ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "UNKNOWN") : parsed.TransactionCode;
        var externalId = mapped ? parsed.TransactionCode : "PHONE:" + native;
        var input = new BankVerifiedInput(original, "COD-NOTIFICATION-" + attestation.Id,
            BankPaymentChannel.Unknown, externalId, parsed.Amount, "COMPLETED", parsed.BankOccurredAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "",
            JsonSerializer.Serialize(new { AttestationId = attestation.Id, receipt.Content }), BankExternalIdentityKind.BankNotification, mapped, exposure,
            JsonSerializer.Deserialize<List<BankCollectionAuthorityProof>>(attestation.CollectionAuthoritySnapshotJson));
        var certification = await db.Set<StorefrontNotificationCertification>().IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.StoreId == Store && x.NotificationAttestationId == attestation.Id, ct);
        var originalGlobal = await FindOriginalGlobalAsync(input, ct);
        int? originalGlobalId = originalGlobal?.Id;
        BankNotificationEvidence.NativeImportReplay? replay = null;
        if (originalGlobal is not null || certification is not null)
        {
            // Validate BEFORE RecordVerifiedAsync: that method independently commits money.
            // This also covers a crash after ledger commit but before certification commit.
            // A registration change must never re-key that notification into a new asset.
            RequireReplayProof(attestation, receipt, certification, originalGlobal, input);
            if (certification is not null) replay = new(receipt.Id, attestation.Id, certification.Id, certification.GlobalBankReceiptId);
        }
        if (await BankNotificationEvidence.ClaimedAsync(db, receipt, null, ct, replay))
            throw new ConflictAppException("Thông báo đã được luồng khác sử dụng; không thể làm nguồn COD mới.");
        var recorded = await new GlobalBankReceiptLedger(db, protocol, clock).RecordVerifiedAsync(input, ct, async token =>
        {
            // Repeat the source preflight inside the ledger's AccountEvidence lock:
            // concurrent first imports cannot evade it by both observing no source yet.
            var freshGlobal = await FindOriginalGlobalAsync(input, token);
            var freshCertification = await db.Set<StorefrontNotificationCertification>().IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.StoreId == Store && x.NotificationAttestationId == attestation.Id, token);
            if (originalGlobalId.HasValue && freshGlobal?.Id != originalGlobalId)
                throw new ConflictAppException("Nguồn ngân hàng nguyên thủy đã thay đổi; không tạo khoản tiền mới.");
            if (freshGlobal is not null || freshCertification is not null)
            {
                RequireReplayProof(attestation, receipt, freshCertification, freshGlobal, input);
                originalGlobalId = freshGlobal!.Id;
            }
            replay = freshCertification is null ? null : new(receipt.Id, attestation.Id, freshCertification.Id, freshCertification.GlobalBankReceiptId);
            // No other related notification/certification or POS claim is exempt.
            if (await BankNotificationEvidence.ClaimedAsync(db, receipt, null, token, replay))
                throw new ConflictAppException("Thông báo đã được luồng khác sử dụng; kiểm tra lại nguồn tiền.");
            var freshAmbiguity = await BankNotificationEvidence.HasNativeManualAmbiguityAsync(db, receipt, attestation.AccountIdentity, token);
            // RequireReplayProof above checked the protected initial confidence and
            // the exact immutable source. Its already-sealed ambiguity is not a new
            // conflict signal; all source, foreign-claim and fresh checks still run.
            if (freshGlobal is { AssetConfidence: BankReceiptAssetConfidence.TransactionIdentityAmbiguous,
                RoutingProvenance: BankReceiptRoutingProvenance.Unproven }) return false;
            return attestation.HadManualAmbiguity || freshAmbiguity;
        });
        if (originalGlobalId.HasValue && recorded.Id != originalGlobalId.Value)
            throw new ConflictAppException("Kết quả nhập lại không khớp chứng cứ ngân hàng nguyên thủy.");
        // Unknown/mutated dates and configuration remain durable Review; this link never promotes their confidence.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await GlobalBankReceiptLedger.LockAsync(db, "GaoApp:BankAccountEvidence:" + attestation.AccountIdentity, ct);
        await GlobalBankReceiptLedger.LockAsync(db, "GaoApp:BankReceipt:" + GlobalBankReceiptLedger.Hash("ACB|" + attestation.AccountIdentity + "|" + externalId), ct);
        var currentCertification = await db.Set<StorefrontNotificationCertification>().IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.StoreId == Store && x.NotificationAttestationId == attestation.Id, ct);
        var currentGlobal = await FindOriginalGlobalAsync(input, ct);
        if (currentGlobal?.Id != recorded.Id)
            throw new ConflictAppException("Nguồn ngân hàng đã thay đổi trước khi chứng nhận; kiểm tra lại.");
        RequireReplayProof(attestation, receipt, currentCertification, currentGlobal, input);
        if (currentCertification is not null)
        {
            if (currentCertification.GlobalBankReceiptId != recorded.Id || replay is not null && currentCertification.Id != replay.CertificationId)
                throw new ConflictAppException("Liên kết chứng nhận ngân hàng đã thay đổi; kiểm tra lại nguồn tiền.");
        }
        else if (replay is not null)
            throw new ConflictAppException("Không còn liên kết chứng nhận nguyên thủy.");
        else
        {
            db.Add(new StorefrontNotificationCertification { StoreId = Store, NotificationAttestationId = attestation.Id, GlobalBankReceiptId = recorded.Id });
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct); return recorded.Id;
    }

    private async Task<BankVerifiedReceipt?> FindOriginalGlobalAsync(BankVerifiedInput input, CancellationToken ct)
    {
        // This is the server-owned attestation reference, not an external/customer
        // selector. Preserve exact BIN2 reference and byte-length semantics.
        var rows = await db.Set<BankVerifiedReceipt>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.OriginStoreId == Store && x.Provider == "ACB" && x.IdentityKind == BankExternalIdentityKind.BankNotification &&
                x.ProviderReference == input.ProviderReference && EF.Functions.DataLength(x.ProviderReference) == input.ProviderReference.Length * 2)
            .OrderBy(x => x.Id).Take(2).ToListAsync(ct);
        if (rows.Count > 1) throw new ConflictAppException("Có nhiều chứng cứ cho cùng lần tiếp nhận; cần đối soát, không tạo khoản tiền mới.");
        return rows.SingleOrDefault();
    }

    private void RequireReplayProof(StorefrontNotificationAttestation attestation, BankNotificationReceipt receipt,
        StorefrontNotificationCertification? certification, BankVerifiedReceipt? global, BankVerifiedInput input)
    {
        var fingerprint = GlobalBankReceiptLedger.Hash(JsonSerializer.Serialize(new { input.ProviderReference, input.Channel, input.IdentityKind,
            Amount = input.Amount.ToString("G29", CultureInfo.InvariantCulture), input.Status, input.RawBankTime }));
        var bankTime = BankEvidenceTime.Parse(input.RawBankTime);
        var authority = input.NativeCollectionAuthorities is { Count: > 0 and <= 1000 } ? JsonSerializer.Serialize(input.NativeCollectionAuthorities) : null;
        if (attestation.IsDeleted || receipt.IsDeleted || global is null || global.IsDeleted ||
            certification is not null && (certification.IsDeleted || certification.StoreId != Store || certification.NotificationAttestationId != attestation.Id || certification.GlobalBankReceiptId != global.Id) ||
            attestation.StoreId != Store || receipt.StoreId != Store || attestation.BankNotificationReceiptId != receipt.Id ||
            global.OriginStoreId != Store || global.Provider != "ACB" || global.BankAccountId != attestation.BankAccountId ||
            global.AccountIdentity != attestation.AccountIdentity || global.AccountVersion != attestation.AccountVersion ||
            global.IdentityKind != input.IdentityKind || global.ExternalTransactionId != input.ExternalTransactionId ||
            global.CrossProtocolMappingVerified != input.CrossProtocolMappingVerified || global.ProviderReference != input.ProviderReference ||
            global.Channel != BankPaymentChannel.Unknown || global.Amount != input.Amount || global.Status != input.Status ||
            global.RawBankTime != input.RawBankTime || global.Fingerprint != fingerprint || global.TimePrecision != bankTime.Precision ||
            global.BankBusinessDate != bankTime.BusinessDate || global.OccurredAtUtc != bankTime.OccurredAtUtc || global.NativeCollectionAuthorityProofJson != authority ||
            global.AssetConfidence == BankReceiptAssetConfidence.Unproven || global.RoutingProvenance == BankReceiptRoutingProvenance.BoundReference ||
            attestation.HadManualAmbiguity && (global.AssetConfidence == BankReceiptAssetConfidence.CertifiedDistinct || global.RoutingProvenance != BankReceiptRoutingProvenance.Unproven))
            throw new ConflictAppException("Cấu hình hoặc chứng cứ nhập lại không khớp chứng nhận nguyên thủy; không tạo nguồn tiền mới.");
        try
        {
            // The protected full input binds the original raw notification and initial
            // asset proof. Mutable ReviewCode/NeedsReview never authorize this replay.
            using var document = JsonDocument.Parse(protocol.Unprotect(Store, global.ProtectedEvidence));
            var evidence = document.RootElement;
            if (evidence.GetProperty("Account").Deserialize<BankVerifiedAccount>() != input.Account ||
                evidence.GetProperty("ProviderReference").GetString() != input.ProviderReference ||
                evidence.GetProperty("ClaimedChannel").GetInt32() != (int)input.Channel || evidence.GetProperty("IdentityKind").GetInt32() != (int)input.IdentityKind ||
                evidence.GetProperty("CrossProtocolMappingVerified").GetBoolean() != input.CrossProtocolMappingVerified ||
                evidence.GetProperty("OriginalAssetConfidence").GetInt32() != (int)global.AssetConfidence ||
                evidence.GetProperty("RawBankTime").GetString() != input.RawBankTime || evidence.GetProperty("Status").GetString() != input.Status ||
                evidence.GetProperty("Amount").GetString() != input.Amount.ToString("G29", CultureInfo.InvariantCulture) ||
                evidence.GetProperty("Raw").GetString() != input.RawEvidence || evidence.GetProperty("RawSha256").GetString() != GlobalBankReceiptLedger.Hash(input.RawEvidence) ||
                evidence.GetProperty("RawBytes").GetInt32() != Encoding.UTF8.GetByteCount(input.RawEvidence) || evidence.GetProperty("Truncated").GetBoolean())
                throw new ConflictAppException("Chứng cứ được bảo vệ không khớp lần tiếp nhận nguyên thủy.");
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new ConflictAppException("Không xác minh được chứng cứ nhập lại nguyên thủy; không tạo nguồn tiền mới.");
        }
    }
}
