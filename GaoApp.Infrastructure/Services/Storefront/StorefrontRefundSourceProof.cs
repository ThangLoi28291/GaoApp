using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Services.Banking;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Storefront;

internal sealed record StorefrontRefundSourceResult(bool Eligible, string? BlockedReason, int? Owner, DateTime EarliestPayoutAtUtc);

internal static class StorefrontRefundSourceProof
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static StorefrontRefundSourceResult Block(string reason) => new(false, reason, null, default);
    internal static async Task<StorefrontRefundSourceResult> ReadAsync(AppDbContext db, AcbProtocolCore protocol,
        StorefrontOrder order, StorefrontRefundFunding funding, CancellationToken ct)
    {
        if (funding.StoreId != order.StoreId || funding.StorefrontOrderId != order.Id || funding.IsDeleted)
            return Block("Nguồn hoàn không thuộc đơn hoặc không còn nguyên vẹn.");
        if (funding.ReceivingLegalEntityId is not > 0)
            return Block("Thiếu chứng cứ chủ thể nhận tiền của nguồn COD tiền mặt; khoản phải xử lý vẫn được giữ, không suy chủ thể từ kho hoặc ca.");
        if (!await db.LegalEntities.AnyAsync(x => x.StoreId == order.StoreId && x.Id == funding.ReceivingLegalEntityId && x.IsActive, ct))
            return Block("Chủ thể nhận tiền cần được kiểm tra trước khi duyệt hoàn.");
        if (funding.GlobalBankReceiptId is not int globalId) return Block("Nguồn cũ chưa đủ chứng cứ tiền thực nhận và chủ thể; cần đối soát riêng.");
        var row = await db.Set<BankVerifiedReceipt>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == globalId, ct);
        if (row is null || row.IsDeleted || row.OriginStoreId != order.StoreId || row.Provider != "ACB" || row.Amount != funding.Amount ||
            row.Amount <= 0 || decimal.Truncate(row.Amount) != row.Amount || row.AssetConfidence != BankReceiptAssetConfidence.CertifiedDistinct ||
            !new[] { "PAID", "SUCCESS", "COMPLETED", "APPROVED" }.Contains(row.Status, StringComparer.Ordinal))
            return Block("Khoản nhận chưa có chứng cứ tài sản độc lập đúng cửa hàng/số tiền/trạng thái.");
        if (await db.Set<BankReceiptConflict>().IgnoreQueryFilters().AnyAsync(x => x.BankVerifiedReceiptId == globalId, ct))
            return Block("Có chứng cứ giao dịch mâu thuẫn hoặc điều chỉnh; không dùng nhãn Review để cho phép hoàn.");
        StorefrontRefundSourceSnapshot? snapshot;
        JsonDocument original;
        try
        {
            snapshot = JsonSerializer.Deserialize<StorefrontRefundSourceSnapshot>(funding.SourceSnapshotJson, Json);
            original = JsonDocument.Parse(protocol.Unprotect(order.StoreId, row.ProtectedEvidence));
        }
        catch (Exception ex) when (ex is JsonException or CryptographicException or InvalidOperationException)
        { return Block("Không xác minh được chứng cứ tiếp nhận nguyên thủy."); }
        using (original)
        {
            try
            {
                var proof = original.RootElement;
                var account = proof.GetProperty("Account").Deserialize<BankVerifiedAccount>();
                if (snapshot is null || snapshot.GlobalReceiptId != globalId || snapshot.CodReceiptId != funding.CodReceiptId || snapshot.Amount != row.Amount ||
                    snapshot.SourceFingerprint != row.Fingerprint || snapshot.OriginalAccountIdentity != row.AccountIdentity || snapshot.OriginalAccountVersion != row.AccountVersion ||
                    snapshot.ReceivingLegalEntityId != funding.ReceivingLegalEntityId || account is null || account.StoreId != order.StoreId || account.Id != row.BankAccountId ||
                    account.Version != row.AccountVersion || GlobalBankReceiptLedger.AccountIdentity(account.BankCode, account.AccountNumber) != row.AccountIdentity ||
                    Convert.FromBase64String(account.Version).Length != 8 || proof.GetProperty("OriginalAssetConfidence").GetInt32() != (int)BankReceiptAssetConfidence.CertifiedDistinct ||
                    proof.GetProperty("Amount").GetString() != row.Amount.ToString("G29", CultureInfo.InvariantCulture) || proof.GetProperty("Status").GetString() != row.Status ||
                    proof.GetProperty("RawBankTime").GetString() != row.RawBankTime || proof.GetProperty("ProviderReference").GetString() != row.ProviderReference ||
                    proof.GetProperty("ClaimedChannel").GetInt32() != (int)row.Channel || proof.GetProperty("IdentityKind").GetInt32() != (int)row.IdentityKind ||
                    proof.TryGetProperty("HistoricalClaimOverlap", out var legacy) && legacy.ValueKind == JsonValueKind.True ||
                    proof.TryGetProperty("CrossProtocolAmbiguity", out var ambiguous) && ambiguous.ValueKind == JsonValueKind.True ||
                    proof.TryGetProperty("AccountChangedAtIntake", out var changed) && changed.ValueKind == JsonValueKind.True ||
                    proof.GetProperty("Truncated").GetBoolean()) return Block("Chứng cứ tiền gốc không đủ điều kiện độc lập; khoản còn nợ không bị xóa.");
                var inputFingerprint = GlobalBankReceiptLedger.Hash(JsonSerializer.Serialize(new { ProviderReference = row.ProviderReference, Channel = row.Channel,
                    IdentityKind = row.IdentityKind, Amount = row.Amount.ToString("G29", CultureInfo.InvariantCulture), Status = row.Status, RawBankTime = row.RawBankTime }));
                if (inputFingerprint != row.Fingerprint) return Block("Fingerprint khoản nhận không khớp đầu vào được bảo vệ.");
                var parsed = BankEvidenceTime.Parse(row.RawBankTime);
                if (parsed.Precision != row.TimePrecision || parsed.BusinessDate != row.BankBusinessDate || parsed.OccurredAtUtc != row.OccurredAtUtc)
                    return Block("Độ chính xác thời gian khoản nhận không khớp chứng cứ gốc.");
                if (funding.Kind == StorefrontRefundSourceKind.QrBankReceipt)
                {
                    var result = await QrAsync(db, protocol, order, funding, row, snapshot, proof, ct);
                    if (!result.Eligible) return result;
                }
                else
                {
                    var result = await CodAsync(db, order, funding, row, snapshot, ct);
                    if (!result.Eligible) return result;
                }
                var earliest = row.VerifiedAtUtc;
                if (row.TimePrecision == BankTimePrecision.Timestamp && row.OccurredAtUtc.HasValue) earliest = row.OccurredAtUtc.Value;
                else if (row.TimePrecision == BankTimePrecision.BankDay && row.OccurredAtUtc.HasValue)
                {
                    var dayEnd = row.OccurredAtUtc.Value.AddDays(1);
                    if (dayEnd < earliest) earliest = dayEnd;
                }
                return new(true, null, funding.ReceivingLegalEntityId, DateTime.SpecifyKind(earliest, DateTimeKind.Utc));
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
            { return Block("Thiếu thành phần chứng cứ gốc; giữ khoản cần hoàn để đối soát."); }
        }
    }
    private static async Task<StorefrontRefundSourceResult> QrAsync(AppDbContext db, AcbProtocolCore protocol, StorefrontOrder order, StorefrontRefundFunding funding,
        BankVerifiedReceipt row, StorefrontRefundSourceSnapshot snapshot, JsonElement proof, CancellationToken ct)
    {
        var intents = await db.Set<StorefrontPaymentIntent>().IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId == order.StoreId && x.StorefrontOrderId == order.Id).Take(2).ToListAsync(ct);
        if (intents.Count != 1) return Block("Thiếu nguồn QR duy nhất của đơn.");
        var intent = intents[0]; var binding = JsonSerializer.Deserialize<StorefrontBankBinding>(intent.BindingSnapshotJson, Json);
        if (binding is null || row.Channel != BankPaymentChannel.Storefront || row.RoutingProvenance != BankReceiptRoutingProvenance.BoundReference ||
            row.ProviderReference != intent.ProviderReference || row.BankAccountId != binding.BankAccountId || row.AccountIdentity != binding.AccountIdentity ||
            funding.ReceivingLegalEntityId != binding.ReceivingLegalEntityId || snapshot.BindingSnapshotJson != intent.BindingSnapshotJson || intent.AmountDue != order.TotalDue ||
            binding.SourceLegalEntityIds.Count == 0 || binding.AuthorityVersions.Count < binding.SourceLegalEntityIds.Distinct().Count())
            return Block("Nguồn ngân hàng/chủ thể/quyền thu nguyên thủy không khớp đơn.");
        var owners = await db.Set<StorefrontFulfillmentSource>().AsNoTracking().Where(x => x.StoreId == order.StoreId && x.StorefrontOrderId == order.Id)
            .Select(x => x.SourceLegalEntityId).Distinct().OrderBy(x => x).ToListAsync(ct);
        if (!owners.SequenceEqual(binding.SourceLegalEntityIds.Distinct().Order())) return Block("Nguồn chủ thể của đơn không còn khớp bản chụp đã niêm phong.");
        foreach (var authority in binding.AuthorityVersions)
        {
            var parts = authority.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var id) || id <= 0 || Convert.FromBase64String(parts[1]).Length != 8)
                return Block("Thiếu phiên bản quyền thu nguyên thủy.");
        }
        if (await db.Set<StorefrontUnattributedBankReceipt>().IgnoreQueryFilters().AnyAsync(x => x.StoreId == order.StoreId && x.PaymentIntentId == intent.Id, ct) ||
            await db.Set<StorefrontBankReviewRestriction>().IgnoreQueryFilters().AnyAsync(x => x.StoreId == order.StoreId && x.PaymentIntentId == intent.Id, ct))
            return Block("Còn chứng cứ ngân hàng không xác định hoặc hạn chế điều chỉnh bất biến.");
        if (await db.Set<AcbQrNotificationItem>().IgnoreQueryFilters().AnyAsync(x => x.StoreId == order.StoreId &&
            EF.Functions.Collate(x.ProviderOrderId, "Latin1_General_100_BIN2") == intent.ProviderReference && EF.Functions.DataLength(x.ProviderOrderId) == intent.ProviderReference.Length * 2 &&
            (x.TransactionStatus == "ERRORCORRECTED" || x.DebitOrCredit == "debit"), ct))
            return Block("Có thông báo điều chỉnh/ghi nợ cần đối soát; không tự coi khoản nhận còn nguyên.");
        var scanBlocked = await StorefrontBankScanEvidence.BlockedReasonAsync(db, protocol, intent, row.NeedsReview || row.TimePrecision == BankTimePrecision.Unknown, ct);
        if (scanBlocked is not null) return Block(scanBlocked);
        if (row.NeedsReview || row.TimePrecision == BankTimePrecision.Unknown)
        {
            if (!proof.TryGetProperty("RefundReviewProtocolVersion", out var version) || version.GetInt32() != 1 ||
                !proof.TryGetProperty("RefundIntakeObservationVerified", out var covered) || covered.ValueKind != JsonValueKind.True ||
                !proof.TryGetProperty("AccountChangedAtIntake", out var stable) || stable.ValueKind != JsonValueKind.False)
                return Block("Khoản Review lịch sử chưa có chứng cứ đầy đủ về các hạn chế ngân hàng; vẫn giữ nghĩa vụ hoàn chờ đối soát.");
            var fingerprint = proof.GetProperty("RefundReviewObservationFingerprint").GetString();
            if (fingerprint is not { Length: 64 } || !await db.Set<StorefrontBankObservation>().IgnoreQueryFilters().AnyAsync(x => x.StoreId == order.StoreId && x.PaymentIntentId == intent.Id && x.Kind == "Retrieve" && x.Fingerprint == fingerprint, ct))
                return Block("Thiếu quan sát truy xuất xác thực gốc.");
        }
        if (row.TimePrecision == BankTimePrecision.Unknown && (!snapshot.OriginalBankTimeUnknown || row.OccurredAtUtc.HasValue))
            return Block("Không thể áp dụng ngoại lệ chỉ thiếu ngày ngân hàng cho nguồn này.");
        if (row.TimePrecision == BankTimePrecision.Timestamp && (row.OccurredAtUtc < order.CreatedAtUtc.AddMinutes(-2) || row.OccurredAtUtc > row.VerifiedAtUtc.AddMinutes(2)) ||
            row.TimePrecision == BankTimePrecision.BankDay && (row.OccurredAtUtc?.AddDays(1) <= order.CreatedAtUtc || row.OccurredAtUtc > row.VerifiedAtUtc.AddMinutes(2)))
            return Block("Thời gian khoản nhận mâu thuẫn với đơn hoặc quan sát xác thực.");
        var claim = await db.Set<BankReceiptConsumption>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.BankVerifiedReceiptId == row.Id, ct);
        if (claim is not null && (claim.OriginStoreId != order.StoreId || claim.Channel != BankPaymentChannel.Storefront ||
            !await db.Set<StorefrontPaymentAllocation>().IgnoreQueryFilters().AnyAsync(a => a.StoreId == order.StoreId && a.StorefrontOrderId == order.Id &&
                a.Id == claim.StorefrontAllocationId && db.Set<StorefrontBankReceipt>().IgnoreQueryFilters().Any(r => r.StoreId == order.StoreId && r.Id == a.BankReceiptId && r.GlobalBankReceiptId == row.Id), ct)))
            return Block("Khoản nhận đã thuộc sử dụng khác, không phải ngân sách hoàn của đơn.");
        if (await db.Set<StorefrontCodBankRouting>().IgnoreQueryFilters().AnyAsync(x => x.GlobalBankReceiptId == row.Id, ct)) return Block("Khoản nhận đã có tuyến COD khác.");
        return new(true, null, funding.ReceivingLegalEntityId, default);
    }
    private static async Task<StorefrontRefundSourceResult> CodAsync(AppDbContext db, StorefrontOrder order, StorefrontRefundFunding funding,
        BankVerifiedReceipt row, StorefrontRefundSourceSnapshot snapshot, CancellationToken ct)
    {
        if (funding.CodReceiptId is not int codId || row.TimePrecision == BankTimePrecision.Unknown || !row.OccurredAtUtc.HasValue ||
            row.Channel != BankPaymentChannel.Unknown || row.RoutingProvenance != BankReceiptRoutingProvenance.UnmatchedOnly || row.NativeCollectionAuthorityProofJson is null)
            return Block("Nguồn COD chưa đủ thời gian/ngân sách/quyền thu nguyên thủy; không áp dụng ngoại lệ ngày QR.");
        var cod = await db.Set<StorefrontCodReceipt>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == order.StoreId && x.Id == codId && x.StorefrontOrderId == order.Id, ct);
        if (cod is null) return Block("Không tìm thấy nguồn thu COD nguyên thủy.");
        var route = await db.Set<StorefrontCodBankRouting>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == order.StoreId &&
            x.Id == cod.CodBankRoutingId && x.GlobalBankReceiptId == row.Id && x.StorefrontOrderId == order.Id, ct);
        if (cod.IsDeleted || cod.GlobalBankReceiptId != row.Id || cod.Amount != row.Amount || cod.SourceSnapshotJson != snapshot.OriginalCodSnapshotJson || route is null || route.IsDeleted ||
            route.ReceivingLegalEntityId != funding.ReceivingLegalEntityId || await db.Set<BankReceiptConsumption>().IgnoreQueryFilters().AnyAsync(x => x.BankVerifiedReceiptId == row.Id, ct) ||
            await db.Set<BankPaymentReference>().IgnoreQueryFilters().AnyAsync(x => x.Provider == row.Provider && x.Reference == row.ProviderReference, ct))
            return Block("Tuyến nhận COD hoặc nguồn ngân hàng có sử dụng khác.");
        var authorities = JsonSerializer.Deserialize<List<BankCollectionAuthorityProof>>(row.NativeCollectionAuthorityProofJson) ?? [];
        using var routed = JsonDocument.Parse(route.AuthoritySnapshotJson);
        var owners = routed.RootElement.GetProperty("owners").EnumerateArray().Select(x => x.GetInt32()).ToList();
        foreach (var authority in routed.RootElement.GetProperty("authorities").EnumerateArray())
            if (!authorities.Any(p => p.Id == authority.GetProperty("id").GetInt32() && p.BankAccountId == row.BankAccountId && p.ReceivingLegalEntityId == route.ReceivingLegalEntityId &&
                p.SourceLegalEntityId == authority.GetProperty("sourceLegalEntityId").GetInt32() && p.Version == authority.GetProperty("version").GetString()))
                return Block("Quyền thu COD không có từ lúc tiếp nhận tiền.");
        if (owners.Count == 0 || owners.Any(owner => !authorities.Any(a => a.SourceLegalEntityId == owner && a.ReceivingLegalEntityId == route.ReceivingLegalEntityId)))
            return Block("Thiếu chủ thể nguồn trong quyền thu COD nguyên thủy.");
        if (row.IdentityKind == BankExternalIdentityKind.BankNotification && !await db.Set<StorefrontNotificationCertification>().IgnoreQueryFilters().AnyAsync(x => x.StoreId == order.StoreId && x.GlobalBankReceiptId == row.Id, ct))
            return Block("Thiếu chứng nhận nguồn thông báo COD.");
        return new(true, null, funding.ReceivingLegalEntityId, default);
    }
}
