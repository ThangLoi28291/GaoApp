using System.Text.Json;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Services.Banking;
using Microsoft.EntityFrameworkCore;
using static GaoApp.Infrastructure.Services.Banking.AcbProtocolCore;

namespace GaoApp.Infrastructure.Services.Storefront;

public sealed partial class StorefrontBankProcessor
{
    private async Task<bool> NoExpiryBlockers(StorefrontPaymentIntent intent, CancellationToken ct)
    {
        if (await StorefrontBankScanEvidence.BlockedReasonAsync(db, protocol, intent, true, ct) is not null) return false;
        // Any observed money (including uncertain/unattributed money) prevents absence.
        if (await StorefrontOrderProofLocks.BankReceiptsForOrder(db, Store, intent.StorefrontOrderId).IgnoreQueryFilters().AnyAsync(ct) ||
            await db.Set<StorefrontUnattributedBankReceipt>().IgnoreQueryFilters().AnyAsync(x => x.StoreId == Store && x.PaymentIntentId == intent.Id, ct) ||
            await db.Set<StorefrontBankReviewRestriction>().IgnoreQueryFilters().AnyAsync(x => x.StoreId == Store && x.PaymentIntentId == intent.Id, ct) ||
            await db.Set<StorefrontPaymentAllocation>().IgnoreQueryFilters().AnyAsync(x => x.StoreId == Store && x.StorefrontOrderId == intent.StorefrontOrderId, ct) ||
            await db.Set<StorefrontCodReceipt>().IgnoreQueryFilters().AnyAsync(x => x.StoreId == Store && x.StorefrontOrderId == intent.StorefrontOrderId, ct)) return false;
        // A durable callback is evidence to reconcile, never proof of no receipt merely
        // because a retrieve result is empty. Keep its tenant/reference range locked.
        return !await db.Set<GaoApp.Domain.Entities.AcbQrNotificationItem>().IgnoreQueryFilters().AnyAsync(x => x.StoreId == Store &&
            EF.Functions.Collate(x.ProviderOrderId, "Latin1_General_100_BIN2") == intent.ProviderReference &&
            EF.Functions.DataLength(x.ProviderOrderId) == intent.ProviderReference.Length * 2, ct);
    }
    private async Task TryExpireUnpaid(StorefrontPaymentIntent intent, StorefrontQrProcessing state,
        StorefrontBankBinding binding, GaoApp.Domain.Entities.StoreAcbSettings acb, CancellationToken ct)
    {
        var hint = await db.StorefrontOrders.AsNoTracking().SingleAsync(x => x.StoreId == Store && x.Id == intent.StorefrontOrderId, ct);
        // Historical/unclassified Review is not cleared by the absence of new restriction rows.
        if (hint.Phase != StorefrontOrderPhase.AwaitingPayment || hint.PaymentState is StorefrontPaymentState.Review or StorefrontPaymentState.Confirmed ||
            state.State is (int)StorefrontQrState.Review or (int)StorefrontQrState.Paid || intent.ExpiresAtUtc > Now || !await NoExpiryBlockers(intent, ct)) return;
        var scan = await db.Set<StorefrontBankScanCompletion>().AsNoTracking().Where(x => x.StoreId == Store && x.PaymentIntentId == intent.Id)
            .OrderByDescending(x => x.Id).FirstAsync(ct);
        var absence = await StorefrontBankScanEvidence.EvaluateUnpaidOrdersAsync(db, protocol, intent, scan, ct);
        if (!absence.Unpaid)
        {
            if (absence.RescanFrom.HasValue)
            {
                // Only scheduling changes. A real subsequent complete authenticated
                // lookup must prove these dates; no money/confidence/review fact is reset.
                await db.Entry(state).ReloadAsync(ct);
                if (!state.NextScanBankDate.HasValue || state.NextScanBankDate.Value > absence.RescanFrom.Value)
                { state.NextScanBankDate = absence.RescanFrom.Value; await db.SaveChangesAsync(ct); }
            }
            return;
        }
        var trace = state.ProviderTraceNumber.Length > 0 ? state.ProviderTraceNumber : state.TraceNumber;
        // No SQL transaction spans an authenticated provider call.
        var response = await protocol.CallAsync(acb, HttpMethod.Delete, acb.ApiBaseUrl.TrimEnd('/') + "/acb/open/payments/qr-payment/v1/cancellation",
            new { traceNumber = trace, orderId = intent.ProviderReference, amount = intent.AmountDue }, ct);
        await Observe(intent, "Cancellation", response, ct);
        if (Text(Path(response, "responseBody"), "status") != "SUCCESS")
        { state.ReviewCode ??= "RETRY_QR_CANCELLATION_UNCONFIRMED"; await db.SaveChangesAsync(ct); return; }
        var cancellationHash = GlobalBankReceiptLedger.Hash(response.GetRawText());
        var observation = await db.Set<StorefrontBankObservation>().AsNoTracking().SingleAsync(x => x.StoreId == Store && x.PaymentIntentId == intent.Id && x.Fingerprint == cancellationHash, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await StorefrontOrderProofLocks.LockAsync(db, Store, intent.StorefrontOrderId, ct);
        await GlobalBankReceiptLedger.LockAsync(db, $"GaoApp:Storefront:Order:{Store}:{intent.StorefrontOrderId}", ct);
        var order = await db.StorefrontOrders.SingleAsync(x => x.StoreId == Store && x.Id == intent.StorefrontOrderId, ct); await db.Entry(order).ReloadAsync(ct);
        await db.Entry(state).ReloadAsync(ct); await db.Entry(intent).ReloadAsync(ct);
        var latest = await db.Set<StorefrontBankScanCompletion>().AsNoTracking().Where(x => x.StoreId == Store && x.PaymentIntentId == intent.Id).OrderByDescending(x => x.Id).Select(x => x.Id).FirstAsync(ct);
        var physical = await StorefrontCancellationFacts.ReadAsync(db, Store, order.Id, ct);
        if (latest != scan.Id || order.Phase != StorefrontOrderPhase.AwaitingPayment || order.PaymentState is StorefrontPaymentState.Review or StorefrontPaymentState.Confirmed ||
            state.State is (int)StorefrontQrState.Review or (int)StorefrontQrState.Paid || physical.HasDispatch || physical.HasIncident || physical.PendingRequest is not null ||
            !await NoExpiryBlockers(intent, ct) || !await StorefrontBankScanEvidence.ContainsOnlyUnpaidOrdersAsync(db, protocol, intent, scan, ct)) { await tx.RollbackAsync(ct); return; }
        var raw = JsonSerializer.Serialize(new { ProtocolVersion = 1, intent.Id, intent.StorefrontOrderId, intent.ProviderReference, intent.AmountDue,
            binding.AccountIdentity, BindingFingerprint = GlobalBankReceiptLedger.Hash(intent.BindingSnapshotJson), ScanCompletionId = scan.Id,
            ScanFingerprint = scan.Fingerprint, CancellationObservationId = observation.Id, CancellationFingerprint = observation.Fingerprint,
            TraceNumber = trace, CancellationStatus = "SUCCESS", VerifiedAtUtc = Now }, Json);
        db.Add(new StorefrontQrClosureProof { StoreId = Store, StorefrontOrderId = order.Id, PaymentIntentId = intent.Id,
            ScanCompletionId = scan.Id, CancellationObservationId = observation.Id, ProviderReference = intent.ProviderReference,
            AccountIdentity = binding.AccountIdentity, Fingerprint = GlobalBankReceiptLedger.Hash(raw), ProtectedEvidence = protocol.Protect(Store, raw), VerifiedAtUtc = Now });
        await db.SaveChangesAsync(ct);
        var transition = StorefrontWorkflowPolicy.Apply(order.Phase, StorefrontCommand.ExpireUnpaidQr, new StorefrontWorkflowFacts {
            StoreId = Store, OrderId = order.Id, Revision = order.Revision, Method = order.PaymentMethod,
            PaymentWindowExpired = intent.ExpiresAtUtc <= Now, BankConfirmedNoReceipt = true },
            new(Store, StorefrontActorKind.System, null, StorefrontCapability.ProcessBankEvents), order.Revision);
        await StorefrontCancellationFacts.ReleaseOwnHoldsAsync(db, new InventoryBalanceRepository(db), order, Now, "QR hết hạn, ngân hàng xác nhận không nhận tiền và đã hủy.", ct);
        await db.SaveChangesAsync(ct);
        order.Phase = transition.NextPhase; order.Revision++; intent.IsActive = false;
        var eventId = Guid.NewGuid(); var payload = JsonSerializer.Serialize(new { intent.Id, ScanCompletionId = scan.Id, CancellationObservationId = observation.Id }, Json);
        db.Add(new StorefrontEvent { StoreId = Store, StorefrontOrderId = order.Id, EventId = eventId, Revision = order.Revision, Kind = "UnpaidQrExpired", PayloadJson = payload });
        db.Add(new StorefrontOutboxMessage { StoreId = Store, StorefrontOrderId = order.Id, EventId = eventId, Revision = order.Revision, Kind = "UnpaidQrExpired", PayloadJson = payload });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
