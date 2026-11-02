using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.Acb;

/// <summary>One bank transfer remains one piece of evidence, even when its notification changes.</summary>
internal static class BankNotificationEvidence
{
    // Financial uncertainty only; amount/time never create a cross-protocol transfer identity.
    internal static async Task<bool> HasNativeManualAmbiguityAsync(AppDbContext db, BankNotificationReceipt receipt, string accountIdentity, CancellationToken ct)
    {
        var occurred = OccurredAt(receipt); var parsed = BankNotificationParser.Parse(receipt.Content);
        var candidates = await db.PosPaymentQrRequests.FromSqlInterpolated($"SELECT * FROM [PosPaymentQrRequests] WITH(HOLDLOCK) WHERE [Amount]={receipt.Amount}")
            .IgnoreQueryFilters().AsNoTracking().Where(q => q.CreatedAtUtc <= occurred.AddSeconds(5) && q.ExpireAtUtc >= occurred.AddSeconds(-5) &&
                q.ConfirmMode == GaoApp.Domain.Enums.BankQrConfirmMode.Manual && q.QrRenderMode == GaoApp.Domain.Enums.BankQrRenderMode.LocalEmvQr &&
                (q.NotificationConfirmationSource == BankNotificationBridgeService.ManualReviewSource || q.NotificationConfirmationSource == BankNotificationBridgeService.ManualCompletedSource ||
                q.NotificationConfirmationSource == BankNotificationBridgeService.AdminApprovalSource || q.NotificationConfirmationSource == BankNotificationBridgeService.AdminCompletedSource ||
                q.NotificationConfirmationSource == BankNotificationBridgeService.AdminReceiptApprovalSource || q.NotificationConfirmationSource == BankNotificationBridgeService.AdminReceiptCompletedSource ||
                q.Status == GaoApp.Domain.Enums.PosPaymentQrStatus.Cancelled || q.Status == GaoApp.Domain.Enums.PosPaymentQrStatus.ManualConfirmed && q.NotificationReceiptId == null))
            .OrderBy(q => q.Id).Take(201).ToListAsync(ct);
        if (candidates.Count > 200) return true;
        foreach (var q in candidates)
        {
            if (parsed?.OrderId is int orderId && orderId != q.OrderId || parsed?.QrCode is string code && !string.Equals(code, q.RequestCode, StringComparison.OrdinalIgnoreCase)) continue;
            var identity = q.AccountIdentitySnapshot;
            if (string.IsNullOrEmpty(identity)) identity = await db.Set<BankNotificationWait>().IgnoreQueryFilters().AsNoTracking()
                .Where(w => w.StoreId == q.StoreId && w.QrId == q.Id).Select(w => w.BankAccountFingerprint).FirstOrDefaultAsync(ct);
            if (string.IsNullOrEmpty(identity))
            {
                var snapshot = await db.Set<AcbQrSession>().IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId == q.StoreId && x.QrRequestId == q.Id)
                    .Select(x => x.BankBindingSnapshotJson).FirstOrDefaultAsync(ct);
                try { if (!string.IsNullOrEmpty(snapshot)) { var original = System.Text.Json.JsonSerializer.Deserialize<GaoApp.Application.Storefront.BankVerifiedAccount>(snapshot);
                    if (original is not null) identity = GaoApp.Infrastructure.Services.Banking.GlobalBankReceiptLedger.AccountIdentity(original.BankCode, original.AccountNumber); } }
                catch (System.Text.Json.JsonException) { }
            }
            // No original binding is explicit uncertainty, even if a mutable current account now differs.
            if (string.IsNullOrEmpty(identity) || identity == accountIdentity) return true;
        }
        return false;
    }
    internal static DateTime OccurredAt(BankNotificationReceipt receipt) =>
        BankNotificationParser.Parse(receipt.Content)?.BankOccurredAtUtc ?? receipt.PostedAtUtc;

    internal static bool SameTransfer(BankNotificationReceipt left, BankNotificationReceipt right)
    {
        if (left.EvidenceKey == right.EvidenceKey && left.EvidenceKey.Length > 0) return true;
        var a = BankNotificationParser.Parse(left.Content);
        var b = BankNotificationParser.Parse(right.Content);
        var ar = a?.BankReferences ?? [left.TransactionCode];
        var br = b?.BankReferences ?? [right.TransactionCode];
        if (ar.Intersect(br).Any(x => !x.StartsWith("MGD:", StringComparison.Ordinal))) return true;
        if (!ar.Intersect(br).Any(x => x.StartsWith("MGD:", StringComparison.Ordinal))) return false;
        if (a?.BankOccurredAtUtc is { } at && b?.BankOccurredAtUtc is { } bt) return at == bt;
        // MGD is only a fallback within one account/business date. Two different
        // strong references must not be merged merely because their MGD matches.
        if (ar.Any(x => !x.StartsWith("MGD:", StringComparison.Ordinal)) &&
            br.Any(x => !x.StartsWith("MGD:", StringComparison.Ordinal))) return false;
        return OccurredAt(left).AddHours(7).Date == OccurredAt(right).AddHours(7).Date;
    }

    internal static async Task<List<BankNotificationReceipt>> RelatedAsync(AppDbContext db, BankNotificationReceipt receipt, CancellationToken ct)
    {
        var fingerprint = await db.Set<BankNotificationDevice>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.StoreId == receipt.StoreId && x.Id == receipt.DeviceId).Select(x => x.BankAccountFingerprint).SingleOrDefaultAsync(ct);
        var knownAccount = !string.IsNullOrEmpty(fingerprint);
        var all = db.Set<BankNotificationReceipt>().IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId == receipt.StoreId &&
            (knownAccount && db.Set<BankNotificationDevice>().IgnoreQueryFilters()
                .Any(d => d.StoreId == receipt.StoreId && d.Id == x.DeviceId && d.BankAccountFingerprint == fingerprint) ||
             !knownAccount && x.BankAccountId == receipt.BankAccountId));
        var refs = BankNotificationParser.Parse(receipt.Content)?.BankReferences ?? [receipt.TransactionCode];
        var query = all.Where(x => x.Id == receipt.Id || x.EvidenceKey == receipt.EvidenceKey);
        foreach (var reference in refs)
        {
            var token = reference.StartsWith("FT", StringComparison.Ordinal) ? reference : reference[(reference.IndexOf(':') + 1)..];
            var matching = all.Where(x => x.TransactionCode == reference || x.Content.ToUpper().Contains(token));
            if (reference.StartsWith("MGD:", StringComparison.Ordinal))
            {
                var time = OccurredAt(receipt);
                // Incoming notifications are at most two days old. Strong bank
                // references above deliberately have no date limit.
                var start = time.Date.AddDays(-2); var end = time.Date.AddDays(3);
                matching = matching.Where(x => x.PostedAtUtc >= start && x.PostedAtUtc < end);
            }
            query = query.Union(matching);
        }
        return (await query.ToListAsync(ct)).Where(x => SameTransfer(receipt, x)).ToList();
    }

    // Only the native importer constructs this after checking the entire original
    // attestation/certification/global-receipt proof. Legacy callers never exempt one.
    internal sealed record NativeImportReplay(int ReceiptId, int AttestationId, int CertificationId, int GlobalReceiptId);

    internal static async Task<bool> ClaimedAsync(AppDbContext db, BankNotificationReceipt receipt, int? retryWaitId, CancellationToken ct,
        NativeImportReplay? nativeReplay = null)
    {
        var related = await RelatedAsync(db, receipt, ct);
        if (related.Any(x => (x.PaymentId != null || x.AppliedWaitId != null) &&
            !(x.Id == receipt.Id && retryWaitId != null && x.AppliedWaitId == retryWaitId))) return true;
        var ids = related.Select(x => x.Id).ToArray();
        var selfReceipt = nativeReplay?.ReceiptId; var selfAttestation = nativeReplay?.AttestationId;
        var selfCertification = nativeReplay?.CertificationId; var selfGlobal = nativeReplay?.GlobalReceiptId;
        if (await (from certification in db.Set<GaoApp.Domain.Storefront.StorefrontNotificationCertification>().IgnoreQueryFilters().AsNoTracking()
            join attestation in db.Set<GaoApp.Domain.Storefront.StorefrontNotificationAttestation>().IgnoreQueryFilters().AsNoTracking() on certification.NotificationAttestationId equals attestation.Id
            where attestation.StoreId == receipt.StoreId && ids.Contains(attestation.BankNotificationReceiptId) &&
                !(selfReceipt == receipt.Id && attestation.BankNotificationReceiptId == receipt.Id && certification.StoreId == receipt.StoreId &&
                  certification.Id == selfCertification && attestation.Id == selfAttestation && certification.GlobalBankReceiptId == selfGlobal)
            select certification.Id).AnyAsync(ct)) return true;
        // Covers historical audit links too, without clearing/reassigning them.
        return await db.PosPaymentQrRequests.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.StoreId == receipt.StoreId &&
            x.NotificationReceiptId != null && ids.Contains(x.NotificationReceiptId.Value) &&
            (x.PaymentId != null || x.NotificationConfirmationSource == BankNotificationBridgeService.AdminReceiptApprovalSource) &&
            !(retryWaitId != null && x.NotificationReceiptId == receipt.Id && db.Set<BankNotificationWait>().IgnoreQueryFilters()
                .Any(w => w.StoreId == receipt.StoreId && w.Id == retryWaitId && w.QrId == x.Id)), ct);
    }
}
