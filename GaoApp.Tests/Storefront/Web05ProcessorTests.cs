using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using GaoApp.Infrastructure.Services.Storefront;
using GaoApp.Infrastructure.Repositories.Inventory;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb05"), Trait("Category", "StorefrontWeb05")]
public sealed class Web05ProcessorTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Trusted_exact_transfer_without_browser_shift_or_terminal_confirms_once_and_never_finalizes_sale_or_delivery()
    {
        var p = await Web05Prepared.CreateAsync(fixture); var c = p.Case; p.Bank.Pay(p.Reference, 45_000m);
        await c.ProcessAsync(p.IntentId, p.Bank); await c.ProcessAsync(p.IntentId, p.Bank);
        await using var db = c.Context(); var order = await db.StorefrontOrders.SingleAsync();
        Assert.Equal(StorefrontOrderPhase.AwaitingPreparation, order.Phase); Assert.Equal(StorefrontPaymentState.Confirmed, order.PaymentState); Assert.Equal(StorefrontDeliveryState.NotPlanned, order.DeliveryState);
        var allocation = Assert.Single(await db.Set<StorefrontPaymentAllocation>().ToListAsync()); Assert.Equal(45_000m, allocation.Amount); Assert.Equal(p.IntentId, allocation.PaymentIntentId);
        var receipt = Assert.Single(await db.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId).ToListAsync()); Assert.Equal(45_000m, receipt.Amount); Assert.False(receipt.NeedsReview);
        var claim = Assert.Single(await db.Set<BankReceiptConsumption>().Where(x => x.BankVerifiedReceiptId == receipt.Id).ToListAsync()); Assert.Equal(allocation.Id, claim.StorefrontAllocationId); Assert.Null(claim.PosPaymentId);
        Assert.Single(await db.Set<StorefrontEvent>().Where(x => x.Kind == "QrPaymentConfirmed").ToListAsync()); Assert.Single(await db.Set<StorefrontOutboxMessage>().Where(x => x.Kind == "QrPaymentConfirmed").ToListAsync());
        Assert.Equal((int)StorefrontQrState.Paid, (await db.Set<StorefrontQrProcessing>().SingleAsync()).State);
        await NoLegacyEffects(c);
    }

    [Theory]
    [InlineData("1")][InlineData("44999")][InlineData("45001")][InlineData("2147483648")]
    public async Task Under_or_overpayment_preserves_full_received_money_and_requires_review_without_allocation(string amountText)
    {
        var p = await Web05Prepared.CreateAsync(fixture); var amount = decimal.Parse(amountText, CultureInfo.InvariantCulture); p.Bank.Pay(p.Reference, amount);
        var probe = new Web05SaveFailureProbe(); await p.Case.ProcessAsync(p.IntentId, p.Bank, probe); await p.Case.ProcessAsync(p.IntentId, p.Bank, probe);
        await probe.WriteAsync(p.Case, p.IntentId, p.Bank, "under-over-" + amountText);
        await Review(p, amount, 1); await NoLegacyEffects(p.Case);
    }

    [Fact]
    public async Task Wrong_first_amount_then_exact_amount_latches_review_instead_of_releasing_order_for_preparation()
    {
        var p = await Web05Prepared.CreateAsync(fixture); p.Bank.Pay(p.Reference, 10_000m); await p.Case.ProcessAsync(p.IntentId, p.Bank);
        p.Bank.Pay(p.Reference, 45_000m); await p.Case.ProcessAsync(p.IntentId, p.Bank); await p.Case.ProcessAsync(p.IntentId, p.Bank);
        await Review(p, 55_000m, 2); await using var db = p.Case.Context(); Assert.Equal(StorefrontOrderPhase.AwaitingPayment, (await db.StorefrontOrders.SingleAsync()).Phase);
    }

    [Fact]
    public async Task Extra_transfer_after_paid_is_reviewed_without_second_allocation_or_lost_money()
    {
        var p = await Web05Prepared.CreateAsync(fixture); p.Bank.Pay(p.Reference, 45_000m); await p.Case.ProcessAsync(p.IntentId, p.Bank);
        p.Bank.Pay(p.Reference, 1_000m); await p.Case.ProcessAsync(p.IntentId, p.Bank);
        await using var db = p.Case.Context(); Assert.Equal(StorefrontPaymentState.Review, (await db.StorefrontOrders.SingleAsync()).PaymentState);
        Assert.Equal(46_000m, await db.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == p.Case.StoreId).SumAsync(x => x.Amount));
        Assert.Equal(45_000m, Assert.Single(await db.Set<StorefrontPaymentAllocation>().ToListAsync()).Amount);
        Assert.Single(await db.Set<BankReceiptConsumption>().Where(x => x.OriginStoreId == p.Case.StoreId).ToListAsync()); await NoLegacyEffects(p.Case);
    }

    [Theory]
    [InlineData("beneficiary")][InlineData("merchant")][InlineData("terminal")][InlineData("user")][InlineData("trace")][InlineData("expected-amount")]
    public async Task Authenticated_money_with_explicit_wrong_bank_binding_is_retained_for_review_and_never_allocated(string field)
    {
        var p = await Web05Prepared.CreateAsync(fixture);
        switch (field)
        {
            case "beneficiary": p.Bank.BeneficiaryOverride = "OTHER-PHYSICAL-ACCOUNT"; break;
            case "merchant": p.Bank.MerchantOverride = "OTHER-MERCHANT"; break;
            case "terminal": p.Bank.TerminalOverride = "OTHER-TERMINAL"; break;
            case "user": p.Bank.UserOverride = "OTHER-USER"; break;
            case "trace": p.Bank.TraceOverride = "OTHER-TRACE"; break;
            default: p.Bank.ExpectedAmountOverride = 1; break;
        }
        p.Bank.Pay(p.Reference, 45_000m); await p.Case.ProcessAsync(p.IntentId, p.Bank);
        if (field != "beneficiary") await Review(p, 45_000m, 1);
        else
        {
            await p.Case.ProcessAsync(p.IntentId, p.Bank); await using var db = p.Case.Context();
            Assert.Equal(StorefrontPaymentState.Review, (await db.StorefrontOrders.SingleAsync()).PaymentState);
            var unattributed = Assert.Single(await db.Set<StorefrontUnattributedBankReceipt>().ToListAsync()); Assert.Equal(45_000m, unattributed.Amount);
            Assert.Equal(p.Case.Registration.AccountIdentity, unattributed.RequestedAccountIdentity); Assert.NotEqual(unattributed.RequestedAccountIdentity, unattributed.ReportedAccountIdentity);
            Assert.Empty(await db.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == p.Case.StoreId).ToListAsync()); Assert.Empty(await db.Set<StorefrontPaymentAllocation>().ToListAsync());
            await Web05CashFlowTests.SnapshotIsolation(p.Case.Seed); var report = await Web05CashFlowTests.Flow(p.Case.Seed, p.Case.Web.Clock.Utc.AddHours(7).Date);
            Assert.Equal(0, report.Summary.Single(x => x.Fund == "bank").Inflow); Assert.NotNull(report.BankReceiptReviews); Assert.Equal(45_000m, Assert.Single(report.BankReceiptReviews).Amount);
            Assert.Contains(p.Bank.Orders[p.Reference].Transfers.Single().ExternalId, Web05Support.AdminProtocol(p.Case.AdminKeys).Unprotect(p.Case.StoreId, unattributed.ProtectedEvidence));
        }
    }

    [Theory]
    [InlineData("current-day", false)][InlineData("old-day", true)][InlineData("future-day", true)]
    [InlineData("pre-intent-timestamp", true)][InlineData("future-timestamp", true)][InlineData("unknown", true)]
    [InlineData("expired-day-interval", true)][InlineData("expired-timestamp", true)]
    public async Task Bank_time_precision_and_cutoff_determine_acceptance_without_using_processing_time_as_payment_time(string timeCase, bool review)
    {
        var p = await Web05Prepared.CreateAsync(fixture); var original = p.Case.Web.Clock.Utc; string raw;
        switch (timeCase)
        {
            case "current-day": raw = original.AddHours(7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); break;
            case "old-day": raw = original.AddDays(-2).AddHours(7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); break;
            case "future-day": raw = original.AddDays(2).AddHours(7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); break;
            case "pre-intent-timestamp": raw = original.AddMinutes(-5).ToString("O", CultureInfo.InvariantCulture); break;
            case "future-timestamp": raw = original.AddMinutes(5).ToString("O", CultureInfo.InvariantCulture); break;
            case "unknown": raw = "unknown-bank-date"; break;
            case "expired-day-interval": p.Case.Web.Clock.Utc = original.AddMinutes(31); raw = original.AddMinutes(31).AddHours(7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); break;
            default: p.Case.Web.Clock.Utc = original.AddMinutes(32); raw = original.AddMinutes(31).ToString("O", CultureInfo.InvariantCulture); break;
        }
        p.Bank.Pay(p.Reference, 45_000m, bankTime: raw); await p.Case.ProcessAsync(p.IntentId, p.Bank);
        if (review) await Review(p, 45_000m, 1);
        else
        {
            await using var db = p.Case.Context(); Assert.Equal(StorefrontPaymentState.Confirmed, (await db.StorefrontOrders.SingleAsync()).PaymentState);
            Assert.Single(await db.Set<StorefrontPaymentAllocation>().ToListAsync()); var receipt = await db.Set<BankVerifiedReceipt>().SingleAsync(x => x.OriginStoreId == p.Case.StoreId);
            Assert.Equal(BankTimePrecision.BankDay, receipt.TimePrecision); Assert.Equal(raw, receipt.RawBankTime); Assert.Equal(original.AddHours(7).Date, receipt.BankBusinessDate);
        }
    }

    [Fact]
    public async Task Allocation_insert_failure_rolls_back_business_effects_but_committed_bank_receipt_survives_then_restart_allocates_once()
    {
        var p = await Web05Prepared.CreateAsync(fixture); p.Bank.Pay(p.Reference, 45_000m); var failure = new FailAllocationInsert();
        await p.Case.ProcessAsync(p.IntentId, p.Bank, failure); Assert.Equal(1, failure.Failures);
        int receiptId;
        await using (var db = p.Case.Context())
        {
            var receipt = Assert.Single(await db.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == p.Case.StoreId).ToListAsync()); receiptId = receipt.Id; Assert.Equal(45_000m, receipt.Amount);
            Assert.Equal(StorefrontOrderPhase.AwaitingPayment, (await db.StorefrontOrders.SingleAsync()).Phase);
            Assert.Empty(await db.Set<StorefrontPaymentAllocation>().ToListAsync()); Assert.Empty(await db.Set<StorefrontBankReceipt>().ToListAsync());
            Assert.Empty(await db.Set<BankReceiptConsumption>().Where(x => x.BankVerifiedReceiptId == receiptId).ToListAsync()); Assert.Single(await db.InventoryReservations.ToListAsync());
        }
        await p.Case.ProcessAsync(p.IntentId, p.Bank); await p.Case.ProcessAsync(p.IntentId, p.Bank);
        await using var check = p.Case.Context(); Assert.Equal(receiptId, Assert.Single(await check.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == p.Case.StoreId).ToListAsync()).Id);
        Assert.Single(await check.Set<StorefrontPaymentAllocation>().ToListAsync()); Assert.Single(await check.Set<BankReceiptConsumption>().Where(x => x.BankVerifiedReceiptId == receiptId).ToListAsync());
        Assert.Equal(StorefrontPaymentState.Confirmed, (await check.StorefrontOrders.SingleAsync()).PaymentState); await NoLegacyEffects(p.Case);
    }

    [Theory]
    [InlineData("account-number")][InlineData("deactivate")][InlineData("soft-delete")]
    public async Task Actual_lookup_response_racing_admin_account_edit_blocks_edit_and_keeps_original_receipt_identity_without_duplicate(string mutation)
    {
        var p = await Web05Prepared.CreateAsync(fixture); var c = p.Case; p.Bank.Pay(p.Reference, 45_000m); var changes = 0; var blocked = false;
        p.Bank.AfterTrustedLookup = async () =>
        {
            if (Interlocked.Increment(ref changes) != 1) return;
            await using var admin = c.Context(); var bank = admin.StoreBankAccounts.Where(x => x.Id == c.Seed.BankAccountId);
            var error = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(async () => {
                if (mutation == "account-number") await bank.ExecuteUpdateAsync(x => x.SetProperty(b => b.AccountNumber, "DIFFERENT-PHYSICAL-ACCOUNT"));
                else if (mutation == "deactivate") await bank.ExecuteUpdateAsync(x => x.SetProperty(b => b.IsActive, false));
                else await bank.ExecuteUpdateAsync(x => x.SetProperty(b => b.IsDeleted, true));
            }); Assert.Equal(51150, error.Number); blocked = true;
        };
        await c.ProcessAsync(p.IntentId, p.Bank); p.Bank.AfterTrustedLookup = null; await c.ProcessAsync(p.IntentId, p.Bank);
        Assert.True(blocked); await using var check = c.Context(); var receipt = await check.Set<BankVerifiedReceipt>().SingleAsync(x => x.OriginStoreId == c.StoreId);
        Assert.Equal(c.Registration.AccountIdentity, receipt.AccountIdentity); Assert.Equal(c.Seed.BankAccountId, receipt.BankAccountId); Assert.Equal(c.StoreId, receipt.OriginStoreId); Assert.Equal(1, changes);
        Assert.Equal(StorefrontPaymentState.Confirmed, (await check.StorefrontOrders.SingleAsync()).PaymentState); Assert.Equal(45_000m, Assert.Single(await check.Set<StorefrontPaymentAllocation>().ToListAsync()).Amount);
    }

    [Fact]
    public async Task Late_money_on_cancelled_order_preserves_cancellation_and_creates_refund_review_without_reopening()
    {
        var p = await Web05Prepared.CreateAsync(fixture);
        // Reach the cancelled state through the current guest command before the original late-bank stimulus.
        await CancelGuestAsync(p);
        p.Bank.Pay(p.Reference, 45_000m); await p.Case.ProcessAsync(p.IntentId, p.Bank); await p.Case.ProcessAsync(p.IntentId, p.Bank);
        await Review(p, 45_000m, 1); await using var check = p.Case.Context(); var order = await check.StorefrontOrders.SingleAsync(); Assert.Equal(StorefrontOrderPhase.Cancelled, order.Phase);
        Assert.Equal(StorefrontRefundState.Requested, order.RefundState); var refund = Assert.Single(await check.Set<StorefrontRefund>().ToListAsync()); Assert.Equal(45_000m, refund.Amount); Assert.Equal(StorefrontRefundState.Requested, refund.State);
        await NoLegacyEffects(p.Case);
    }

    [Fact]
    public async Task Same_intent_parallel_processors_can_only_allocate_one_receipt_once()
    {
        var p = await Web05Prepared.CreateAsync(fixture); p.Bank.Pay(p.Reference, 45_000m);
        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => p.Case.ProcessAsync(p.IntentId, p.Bank)));
        await using var db = p.Case.Context(); Assert.Single(await db.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == p.Case.StoreId).ToListAsync());
        Assert.Single(await db.Set<StorefrontPaymentAllocation>().ToListAsync()); Assert.Single(await db.Set<BankReceiptConsumption>().Where(x => x.OriginStoreId == p.Case.StoreId).ToListAsync());
        Assert.Equal(StorefrontPaymentState.Confirmed, (await db.StorefrontOrders.SingleAsync()).PaymentState); Assert.Single(p.Bank.Orders); Assert.Equal(1, p.Bank.InitiateCalls);
    }

    [Fact]
    public async Task Authenticated_callback_correlation_is_tenant_scoped_and_summary_amount_cannot_replace_trusted_bank_retrieval()
    {
        var p = await Web05Prepared.CreateAsync(fixture); var foreign = await Web05Prepared.CreateAsync(fixture); var calls = p.Bank.RetrieveCalls;
        static JsonElement Payload(string reference, int fakeStore) => JsonSerializer.SerializeToElement(new { StoreId = fakeStore, requestParameters = new { request = new { requestParams = new {
            transactions = new[] { new { amount = 1, transactionStatus = "COMPLETED", transactionEntityAttribute = new { custom4 = reference } } } } } } });
        await using (var db = p.Case.Context())
        {
            var processor = new StorefrontBankProcessor(db, Web05Support.AdminProtocol(p.Case.AdminKeys, p.Bank), p.Case.Web.Clock);
            await processor.ProcessCallbackAsync(Payload(foreign.Reference, foreign.Case.StoreId));
            await processor.ProcessCallbackAsync(Payload(p.OrderId.ToString(CultureInfo.InvariantCulture), p.Case.StoreId));
            await processor.ProcessCallbackAsync(Payload("GA000000000001", p.Case.StoreId));
            await processor.ProcessCallbackAsync(Payload("W999999999999", p.Case.StoreId));
        }
        Assert.Equal(calls, p.Bank.RetrieveCalls);
        p.Bank.Pay(p.Reference, 45_000m);
        await using (var db = p.Case.Context())
            await new StorefrontBankProcessor(db, Web05Support.AdminProtocol(p.Case.AdminKeys, p.Bank), p.Case.Web.Clock).ProcessCallbackAsync(Payload(p.Reference, foreign.Case.StoreId));
        await using var check = p.Case.Context(); Assert.Equal(StorefrontPaymentState.Confirmed, (await check.StorefrontOrders.SingleAsync()).PaymentState);
        Assert.Equal(45_000m, Assert.Single(await check.Set<StorefrontPaymentAllocation>().ToListAsync()).Amount);
        await using var other = foreign.Case.Context(); Assert.Equal(StorefrontPaymentState.Pending, (await other.StorefrontOrders.SingleAsync()).PaymentState); Assert.Empty(await other.Set<StorefrontPaymentAllocation>().ToListAsync());
    }

    [Theory]
    [InlineData("REVERSED")][InlineData("CANCELLED")]
    public async Task Trusted_bank_correction_after_confirmation_is_durable_review_and_later_old_success_cannot_clear_it(string correctedStatus)
    {
        var p = await Web05Prepared.CreateAsync(fixture); p.Bank.Pay(p.Reference, 45_000m); await p.Case.ProcessAsync(p.IntentId, p.Bank);
        var order = p.Bank.Orders[p.Reference]; lock (order.Transfers) order.Transfers[0] = order.Transfers[0] with { Status = correctedStatus };
        await p.Case.ProcessAsync(p.IntentId, p.Bank); await p.Case.ProcessAsync(p.IntentId, p.Bank);
        await using (var check = p.Case.Context())
        {
            Assert.Equal(StorefrontPaymentState.Review, (await check.StorefrontOrders.SingleAsync()).PaymentState);
            var receipt = await check.Set<BankVerifiedReceipt>().SingleAsync(x => x.OriginStoreId == p.Case.StoreId); Assert.Equal(45_000m, receipt.Amount); Assert.True(receipt.NeedsReview);
            Assert.Single(await check.Set<StorefrontPaymentAllocation>().ToListAsync()); Assert.Single(await check.Set<BankReceiptConsumption>().Where(x => x.OriginStoreId == p.Case.StoreId).ToListAsync());
            var corrections = await check.Set<BankReceiptConflict>().Where(x => x.BankVerifiedReceiptId == receipt.Id).Select(x => x.ProtectedEvidence).ToListAsync();
            corrections.AddRange(await check.Set<StorefrontBankObservation>().Where(x => x.PaymentIntentId == p.IntentId).Select(x => x.ProtectedEvidence).ToListAsync());
            var protocol = Web05Support.AdminProtocol(p.Case.AdminKeys); Assert.Contains(corrections, x => protocol.Unprotect(p.Case.StoreId, x).Contains(correctedStatus, StringComparison.Ordinal));
        }
        lock (order.Transfers) order.Transfers[0] = order.Transfers[0] with { Status = "COMPLETED" };
        await p.Case.ProcessAsync(p.IntentId, p.Bank); await using var final = p.Case.Context(); Assert.Equal(StorefrontPaymentState.Review, (await final.StorefrontOrders.SingleAsync()).PaymentState);
        Assert.Single(await final.Set<StorefrontPaymentAllocation>().ToListAsync());
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Aged_intent_bank_history_is_retrieved_in_bounded_windows_and_late_current_money_is_retained_without_reopening(bool cancelled)
    {
        var p = await Web05Prepared.CreateAsync(fixture); p.Bank.RespectQueryWindows = true;
        if (cancelled) await CancelGuestAsync(p);
        p.Case.Web.Clock.Utc = p.Case.Web.Clock.Utc.AddDays(45); p.Bank.Pay(p.Reference, 45_000m);
        // The transport rejects every >31-day query and only returns transfers
        // whose actual bank date lies in that specific requested window.
        var probe = new Web05SaveFailureProbe(); await p.Case.ProcessAsync(p.IntentId, p.Bank, probe); await p.Case.ProcessAsync(p.IntentId, p.Bank, probe);
        await probe.WriteAsync(p.Case, p.IntentId, p.Bank, "aged45days-cancelled-" + cancelled);
        await Review(p, 45_000m, 1); await using var check = p.Case.Context(); Assert.Single(await check.Set<StorefrontPaymentIntent>().ToListAsync()); Assert.Single(p.Bank.Orders); Assert.Equal(1, p.Bank.InitiateCalls);
        Assert.Equal(cancelled ? StorefrontOrderPhase.Cancelled : StorefrontOrderPhase.AwaitingPayment, (await check.StorefrontOrders.SingleAsync()).Phase);
        await NoLegacyEffects(p.Case);
    }

    [Fact]
    public async Task One_trusted_lookup_containing_exact_plus_extra_money_is_whole_batch_review_without_a_transient_success_allocation()
    {
        var p = await Web05Prepared.CreateAsync(fixture); p.Bank.Pay(p.Reference, 45_000m); p.Bank.Pay(p.Reference, 1_000m);
        await p.Case.ProcessAsync(p.IntentId, p.Bank); await Review(p, 46_000m, 2);
        await using var db = p.Case.Context(); Assert.Empty(await db.Set<StorefrontEvent>().Where(x => x.Kind == "QrPaymentConfirmed").ToListAsync());
        Assert.Empty(await db.Set<StorefrontOutboxMessage>().Where(x => x.Kind == "QrPaymentConfirmed").ToListAsync()); Assert.Equal(StorefrontOrderPhase.AwaitingPayment, (await db.StorefrontOrders.SingleAsync()).Phase);
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Known_batch_anomaly_is_reviewed_before_second_receipt_failure_even_if_previous_lookup_had_confirmed(bool previouslyPaid)
    {
        var p = await Web05Prepared.CreateAsync(fixture); var exact = "W05-EXACT-" + Guid.NewGuid().ToString("N"); var extra = "W05-EXTRA-" + Guid.NewGuid().ToString("N");
        p.Bank.Pay(p.Reference, 45_000m, exact); if (previouslyPaid) await p.Case.ProcessAsync(p.IntentId, p.Bank);
        p.Bank.Pay(p.Reference, 1_000m, extra); var fault = new FailReceiptInsert(previouslyPaid ? 1 : 2);
        await p.Case.ProcessAsync(p.IntentId, p.Bank, fault); Assert.True(fault.Triggered);
        await using (var check = p.Case.Context())
        {
            Assert.Equal(StorefrontPaymentState.Review, (await check.StorefrontOrders.SingleAsync()).PaymentState);
            Assert.Single(await check.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == p.Case.StoreId).ToListAsync());
            Assert.Equal(previouslyPaid ? 1 : 0, await check.Set<StorefrontPaymentAllocation>().CountAsync());
            Assert.Equal(previouslyPaid ? 1 : 0, await check.Set<StorefrontOutboxMessage>().CountAsync(x => x.Kind == "QrPaymentConfirmed"));
            var observations = await check.Set<StorefrontBankObservation>().Where(x => x.PaymentIntentId == p.IntentId).Select(x => x.ProtectedEvidence).ToListAsync(); var protocol = Web05Support.AdminProtocol(p.Case.AdminKeys);
            Assert.Contains(observations, x => { var raw = protocol.Unprotect(p.Case.StoreId, x); return raw.Contains(exact, StringComparison.Ordinal) && raw.Contains(extra, StringComparison.Ordinal); });
        }
        await p.Case.ProcessAsync(p.IntentId, p.Bank); await using var final = p.Case.Context();
        Assert.Equal(StorefrontPaymentState.Review, (await final.StorefrontOrders.SingleAsync()).PaymentState);
        Assert.Equal(46_000m, await final.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == p.Case.StoreId).SumAsync(x => x.Amount));
        Assert.Equal(previouslyPaid ? 1 : 0, await final.Set<StorefrontPaymentAllocation>().CountAsync()); Assert.Equal(previouslyPaid ? 1 : 0, await final.Set<BankReceiptConsumption>().CountAsync(x => x.OriginStoreId == p.Case.StoreId));
    }

    private static async Task CancelGuestAsync(Web05Prepared p)
    {
        await using var db = p.Case.Context();
        var service = new StorefrontCancellationService(db, new Web01Actor(null, false), new InventoryBalanceRepository(db), p.Case.Web.Clock);
        var token = Web04Support.GuestToken(p.Placed); var state = await service.GetGuestAsync(token);
        Assert.NotNull(state); Assert.True(state.CanSelfCancel);
        await service.CancelGuestAsync(new StorefrontGuestCancellationRequest
        {
            ClientRequestId = Guid.NewGuid(), ExpectedRevision = state.Revision, ExpectedVersion = state.Version,
            Token = token, Reason = "WEB05 actual guest cancellation before later bank observation"
        });
        Assert.Equal(StorefrontOrderPhase.Cancelled, (await db.StorefrontOrders.AsNoTracking().SingleAsync()).Phase);
    }

    private static async Task Review(Web05Prepared p, decimal fullAmount, int receipts)
    {
        await using var db = p.Case.Context(); Assert.Equal(StorefrontPaymentState.Review, (await db.StorefrontOrders.SingleAsync()).PaymentState);
        Assert.Equal((int)StorefrontQrState.Review, (await db.Set<StorefrontQrProcessing>().SingleAsync()).State);
        var rows = await db.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == p.Case.StoreId).ToListAsync(); Assert.Equal(receipts, rows.Count); Assert.Equal(fullAmount, rows.Sum(x => x.Amount));
        Assert.Empty(await db.Set<StorefrontPaymentAllocation>().ToListAsync()); Assert.Empty(await db.Set<BankReceiptConsumption>().Where(x => x.OriginStoreId == p.Case.StoreId).ToListAsync());
    }
    private static async Task NoLegacyEffects(Web05Case c)
    {
        await using var db = c.Context(); Assert.Empty(await db.Orders.ToListAsync()); Assert.Empty(await db.POSShifts.ToListAsync()); Assert.Empty(await db.POSTerminals.ToListAsync());
        Assert.Empty(await db.OrderPayments.ToListAsync()); Assert.Empty(await db.InventoryTransactions.ToListAsync()); Assert.Empty(await db.AdminMenuItems.ToListAsync());
        Assert.Equal(100m, (await db.InventoryBalances.SingleAsync()).OnHandQty);
    }
    private sealed class FailAllocationInsert : DbCommandInterceptor
    {
        internal int Failures;
        private void Check(DbCommand command)
        {
            if (command.CommandText.Contains("INSERT INTO [StorefrontPaymentAllocations]", StringComparison.Ordinal) && Interlocked.CompareExchange(ref Failures, 1, 0) == 0)
                throw new InvalidOperationException("Synthetic failure after independently committed bank receipt, before allocation INSERT.");
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Check(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Check(command); return ValueTask.FromResult(result); }
    }
    private sealed class FailReceiptInsert(int ordinal) : DbCommandInterceptor
    {
        private int inserts; internal bool Triggered;
        private void Check(DbCommand command)
        {
            if (command.CommandText.Contains("INSERT INTO [BankVerifiedReceipts]", StringComparison.Ordinal) && Interlocked.Increment(ref inserts) == ordinal)
            { Triggered = true; throw new InvalidOperationException("Synthetic failure while ingesting already known multi-transfer response."); }
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Check(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Check(command); return ValueTask.FromResult(result); }
    }
}
