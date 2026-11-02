using System.Text.Json;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Services.Banking;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Storefront;

public sealed partial class StorefrontBankProcessor
{
    private static readonly HashSet<string> RefundRestrictingBankReviews = new(StringComparer.Ordinal)
    {
        "BANK_CORRECTION_NOTIFICATION", "VERIFIED_RECEIPT_CORRECTED", "PREVIOUS_RECEIPT_NOT_CONFIRMED",
        "RETRIEVE_BINDING_MISMATCH", "QR_IDENTITY_CHANGED", "INVALID_RETRIEVE_PAGE", "INVALID_TRANSACTION_LIST",
        "INVALID_TRANSACTION_DETAIL", "INVALID_VERIFIED_TRANSACTION", "BANK_TRANSACTION_INVALID", "EVIDENCE_SIZE_LIMIT",
        "RETRIEVE_PAGINATION_CHANGED"
    };
    private async Task AppendRefundRestriction(StorefrontPaymentIntent intent, string code, JsonElement? callback, CancellationToken ct)
    {
        if (!RefundRestrictingBankReviews.Contains(code)) return;
        string kind; string fingerprint; string evidence;
        if (callback.HasValue)
        {
            kind = "AuthenticatedCallback"; var raw = callback.Value.GetRawText(); fingerprint = GlobalBankReceiptLedger.Hash(raw);
            evidence = protocol.Protect(Store, JsonSerializer.Serialize(new { intent.Id, intent.StorefrontOrderId, intent.ProviderReference,
                Reason = code, EvidenceKind = kind, Fingerprint = fingerprint, Callback = callback.Value }, Json));
        }
        else
        {
            var observation = await db.Set<StorefrontBankObservation>().IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.StoreId == Store && x.PaymentIntentId == intent.Id).OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct)
                ?? throw new InvalidOperationException("A bank restriction needs its actual durable observation.");
            kind = observation.Kind; fingerprint = observation.Fingerprint;
            evidence = protocol.Protect(Store, JsonSerializer.Serialize(new { intent.Id, intent.StorefrontOrderId, intent.ProviderReference,
                Reason = code, ObservationId = observation.Id, observation.Fingerprint,
                ProtectedObservationHash = GlobalBankReceiptLedger.Hash(observation.ProtectedEvidence) }, Json));
        }
        // Stage just this append before receipt/order/processing writes. SaveChanges
        // would flush unrelated tracked caller state and reverse the reader lock order.
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [StorefrontBankReviewRestrictions] ([StoreId],[StorefrontOrderId],[PaymentIntentId],[ReasonCode],[EvidenceKind],[EvidenceFingerprint],[ProtectedEvidence],[IsDeleted]) SELECT {Store},{intent.StorefrontOrderId},{intent.Id},{code},{kind},{fingerprint},{evidence},0 WHERE NOT EXISTS(SELECT 1 FROM [StorefrontBankReviewRestrictions] WITH(UPDLOCK,HOLDLOCK) WHERE [StoreId]={Store} AND [PaymentIntentId]={intent.Id} AND [ReasonCode]={code} AND [EvidenceFingerprint]={fingerprint});", ct);
    }
}
