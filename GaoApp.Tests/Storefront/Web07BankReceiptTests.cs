using GaoApp.Domain.Entities;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb07"), Trait("Category", "StorefrontWeb07")]
public sealed class Web07BankReceiptTests(Web01Fixture fixture)
{
    [Fact]
    public async Task New_distinct_credit_after_an_executed_full_refund_creates_only_its_new_remaining_obligation_and_preserves_actual_paid_history()
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture, prepaidQr: true); var physical = c.Physical!;
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, true).CancelGuestAsync(await c.CancelRequestAsync());
        int originalRefund; await using (var db = c.Context()) originalRefund = (await db.Set<StorefrontRefund>().SingleAsync()).Id;
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ApproveAsync(c.OrderId, await c.RefundDecisionAsync(originalRefund, "Hoàn đủ nguồn nhận nguyên thủy"));
        var debit = await Web07RecordedDebit.BankAsync(c, 230_000m);
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ConfirmExecutionAsync(c.OrderId,
            await c.ExecutionRequestAsync(originalRefund, debit.Source, c.Seed.LegalEntityId, c.Seed.BankAccountId, debit.ExternalReference, debit.ActualExecutionAt));
        physical.QrTransport!.Pay(physical.ProviderReference!, 1_000m); await physical.QrCase!.ProcessAsync(physical.IntentId!.Value, physical.QrTransport);
        await physical.QrCase.ProcessAsync(physical.IntentId.Value, physical.QrTransport);
        await using var db2 = c.Context(); var rows = await Web07Support.Refunds(c, db2).GetAsync(c.OrderId);
        Assert.Equal(2, rows.Count); var old = Assert.Single(rows, x => x.Id == originalRefund);
        Assert.Equal(230_000m, old.Amount); Assert.Equal(230_000m, old.ExecutedAmount); Assert.Equal(StorefrontRefundState.Executed, old.State); Assert.Single(old.Executions);
        var fresh = Assert.Single(rows, x => x.Id != originalRefund); Assert.Equal(1_000m, fresh.Amount); Assert.Equal(0m, fresh.ExecutedAmount); Assert.Equal(StorefrontRefundState.Requested, fresh.State);
        Assert.Equal(231_000m, await db2.Set<StorefrontRefundFunding>().Where(x => x.StorefrontOrderId == c.OrderId).SumAsync(x => x.Amount));
        Assert.Equal(230_000m, await db2.Set<StorefrontRefundEvidence>().Where(x => x.StorefrontOrderId == c.OrderId).SumAsync(x => x.Amount));
        Assert.Equal(StorefrontOrderPhase.Cancelled, (await db2.StorefrontOrders.SingleAsync()).Phase); Assert.Equal(StorefrontRefundState.Requested, (await db2.StorefrontOrders.SingleAsync()).RefundState);
        Assert.Single(await db2.TreasuryEntries.ToListAsync()); Assert.Empty(await db2.OrderPayments.ToListAsync()); Assert.Empty(await db2.Orders.ToListAsync());
        var flow = await Web05CashFlowTests.Flow(c.Seed, DateTime.UtcNow.AddHours(7).Date);
        Assert.Equal(231_000m, flow.Summary.Single(x => x.Fund == "bank").Inflow); Assert.Equal(230_000m, flow.Summary.Single(x => x.Fund == "bank").Outflow);
    }

    [Theory]
    [InlineData("amount-conflict", false)] [InlineData("reversed", false)]
    [InlineData("unattributed", false)] [InlineData("amount-conflict", true)]
    public async Task Fresh_bank_restriction_after_approval_blocks_remaining_execution_without_erasing_prior_actual_partial_payout_or_received_money(string restriction, bool partialAlreadyPaid)
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture, prepaidQr: true); var physical = c.Physical!;
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, true).CancelGuestAsync(await c.CancelRequestAsync());
        int refundId; await using (var db = c.Context()) refundId = (await db.Set<StorefrontRefund>().SingleAsync()).Id;
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ApproveAsync(c.OrderId, await c.RefundDecisionAsync(refundId, "Nguồn ban đầu đã qua kiểm tra, chưa có mâu thuẫn"));
        if (partialAlreadyPaid)
        {
            var prior = await Web07RecordedDebit.BankAsync(c, 100_000m);
            await using var db = c.Context(); await Web07Support.Refunds(c, db).ConfirmExecutionAsync(c.OrderId,
                await c.ExecutionRequestAsync(refundId, prior.Source, c.Seed.LegalEntityId, c.Seed.BankAccountId, prior.ExternalReference, prior.ActualExecutionAt));
        }
        var remaining = partialAlreadyPaid ? 130_000m : 230_000m; var debit = await Web07RecordedDebit.BankAsync(c, remaining);
        var providerOrder = physical.QrTransport!.Orders[physical.ProviderReference!];
        if (restriction == "unattributed") { physical.QrTransport.BeneficiaryOverride = "UNRELATED-RECEIVING-ACCOUNT"; physical.QrTransport.Pay(physical.ProviderReference!, 1_000m); }
        else lock (providerOrder.Transfers)
            providerOrder.Transfers[0] = providerOrder.Transfers[0] with { Amount = restriction == "amount-conflict" ? 230_001m : 230_000m, Status = restriction == "reversed" ? "REVERSED" : "COMPLETED" };
        await physical.QrCase!.ProcessAsync(physical.IntentId!.Value, physical.QrTransport);
        var beforeLabelAttempt = await c.SnapshotAsync();
        await using (var db = c.Context())
        {
            if (restriction == "unattributed")
                await db.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId).ExecuteUpdateAsync(x => x.SetProperty(r => r.ReviewCode, "BANK_TIME_UNKNOWN"));
            else
            {
                var denied = await Assert.ThrowsAsync<SqlException>(() => db.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId)
                    .ExecuteUpdateAsync(x => x.SetProperty(r => r.ReviewCode, "BANK_TIME_UNKNOWN")));
                Assert.Contains(denied.Errors.Cast<SqlError>(), x => x.Number == 51163 && x.Message == "A financial review latch cannot be cleared or downgraded by replay.");
            }
        }
        if (restriction != "unattributed") Assert.Equal(beforeLabelAttempt, await c.SnapshotAsync());
        var request = await c.ExecutionRequestAsync(refundId, debit.Source, c.Seed.LegalEntityId, c.Seed.BankAccountId, debit.ExternalReference, debit.ActualExecutionAt);
        var before = await c.SnapshotAsync(); var flow = await Web07RefundExecutionTests.FlowAsync(c);
        await using (var db = c.Context())
        {
            var refund = Assert.Single(await Web07Support.Refunds(c, db).GetAsync(c.OrderId));
            Assert.Equal(230_000m, refund.Amount); Assert.Equal(partialAlreadyPaid ? 100_000m : 0m, refund.ExecutedAmount);
            Assert.False(refund.CanConfirmExecution); Assert.False(string.IsNullOrWhiteSpace(refund.BlockedReason));
            await Assert.ThrowsAsync<ConflictAppException>(() => Web07Support.Refunds(c, db).ConfirmExecutionAsync(c.OrderId, request));
        }
        Assert.Equal(before, await c.SnapshotAsync()); Assert.Equal(flow, await Web07RefundExecutionTests.FlowAsync(c));
        await using var check = c.Context();
        Assert.Equal(230_000m, await check.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId).SumAsync(x => x.Amount));
        Assert.Equal(partialAlreadyPaid ? 100_000m : 0m, await check.Set<StorefrontRefundEvidence>().Where(x => x.RefundId == refundId).SumAsync(x => x.Amount));
        Assert.NotEqual(StorefrontRefundState.Executed, (await check.Set<StorefrontRefund>().SingleAsync()).State);
        if (restriction == "unattributed")
        {
            // The authenticated header override applies to BOTH entries. Keep
            // the entire contradictory frame as audit, never certified assets.
            var observed = await check.Set<StorefrontUnattributedBankReceipt>().Where(x => x.StoreId == c.StoreId).ToListAsync();
            Assert.Equal(2, observed.Count); Assert.Equal(new[] { 1_000m, 230_000m }, observed.Select(x => x.Amount).Order());
            Assert.Equal(231_000m, observed.Sum(x => x.Amount));
            Assert.Single(await check.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId).ToListAsync());
            var actualFlow = await Web05CashFlowTests.Flow(c.Seed, DateTime.UtcNow.AddHours(7).Date);
            Assert.Equal(230_000m, actualFlow.Summary.Single(x => x.Fund == "bank").Inflow);
        }
    }

    [Fact]
    public async Task Actual_distinct_excess_credit_is_not_capped_to_order_due_and_HTTP_keeps_exact_whole_VND_text_above_javascript_integer_precision()
    {
        const decimal actual = 9_007_199_254_740_993m;
        var session = await Web07BankSession.CreateAsync(fixture); var c = session.Case;
        session.Transport.Pay(session.Reference, actual); await session.ProcessAsync();
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, true).CancelGuestAsync(await c.CancelRequestAsync());
        await using (var db = c.Context())
        {
            var refund = Assert.Single(await db.Set<StorefrontRefund>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
            Assert.Equal(actual, refund.Amount); Assert.Equal(actual, refund.UnallocatedReceiptAmount); Assert.Equal(0m, refund.GoodsAmount); Assert.Equal(0m, refund.ShippingAmount);
            Assert.Equal(actual, await db.Set<StorefrontRefundFunding>().Where(x => x.RefundId == refund.Id).SumAsync(x => x.Amount));
            Assert.Equal(45_000m, (await db.StorefrontOrders.SingleAsync()).TotalDue);
        }
        await using var host = await Web07PublicHost.StartAsync(c); await host.CsrfAsync();
        using var response = await host.Http.PostAsJsonAsync("/shop/orders/cancellation/access", new { token = c.Token });
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = Assert.Single(json.RootElement.GetProperty("refunds").EnumerateArray()); Assert.Equal(actual, row.GetProperty("amount").GetDecimal());
        Assert.Equal("9.007.199.254.740.993 đ", row.GetProperty("amountText").GetString()); Assert.Equal(0m, row.GetProperty("executedAmount").GetDecimal());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Two_genuine_distinct_bound_credits_each_keep_their_own_refund_budget_before_or_after_cancellation_without_clearing_payment_Review(bool extraAfterCancel)
    {
        var session = await Web07BankSession.CreateAsync(fixture); var c = session.Case;
        session.Transport.Pay(session.Reference, 45_000m); await session.ProcessAsync();
        if (!extraAfterCancel) { session.Transport.Pay(session.Reference, 1_000m); await session.ProcessAsync(); }
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, true).CancelGuestAsync(await c.CancelRequestAsync());
        if (extraAfterCancel) { session.Transport.Pay(session.Reference, 1_000m); await session.ProcessAsync(); }
        StorefrontRefundRead[] refunds;
        await using (var db = c.Context())
        {
            refunds = (await Web07Support.Refunds(c, db).GetAsync(c.OrderId)).ToArray(); Assert.Equal(2, refunds.Length);
            Assert.Equal(46_000m, refunds.Sum(x => x.Amount)); Assert.All(refunds, x => { Assert.Equal(StorefrontRefundState.Requested, x.State); Assert.True(x.CanApprove, x.BlockedReason); });
            var originals = await db.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId).ToListAsync();
            Assert.Equal(2, originals.Count); Assert.Equal(46_000m, originals.Sum(x => x.Amount));
            Assert.All(originals, x => Assert.Equal(BankReceiptAssetConfidence.CertifiedDistinct, x.AssetConfidence));
            Assert.Equal(2, await db.Set<StorefrontRefundFunding>().Where(x => x.StorefrontOrderId == c.OrderId).CountAsync());
            Assert.Equal(StorefrontPaymentState.Review, (await db.StorefrontOrders.SingleAsync()).PaymentState);
        }
        foreach (var refund in refunds)
        {
            await using (var db = c.Context()) await Web07Support.Refunds(c, db).ApproveAsync(c.OrderId, await c.RefundDecisionAsync(refund.Id, "Hai khoản khác mã độc lập, không suy diễn là một giao dịch"));
            var debit = await Web07RecordedDebit.BankAsync(c, refund.Amount);
            var command = await c.ExecutionRequestAsync(refund.Id, debit.Source, c.Seed.LegalEntityId, c.Seed.BankAccountId, debit.ExternalReference, debit.ActualExecutionAt);
            var before = await Web07RefundExecutionTests.FlowAsync(c);
            await using (var db = c.Context()) await Web07Support.Refunds(c, db).ConfirmExecutionAsync(c.OrderId, command);
            Assert.Equal(before, await Web07RefundExecutionTests.FlowAsync(c));
        }
        await session.ProcessAsync(); await using var check = c.Context();
        Assert.Equal(2, await check.Set<StorefrontRefund>().Where(x => x.StorefrontOrderId == c.OrderId).CountAsync());
        Assert.Equal(2, await check.Set<StorefrontRefundFunding>().Where(x => x.StorefrontOrderId == c.OrderId).CountAsync());
        Assert.Equal(46_000m, await check.Set<StorefrontRefundEvidence>().Where(x => x.StorefrontOrderId == c.OrderId).SumAsync(x => x.Amount));
        Assert.Equal(StorefrontPaymentState.Review, (await check.StorefrontOrders.SingleAsync()).PaymentState);
        Assert.Equal(StorefrontOrderPhase.Cancelled, (await check.StorefrontOrders.SingleAsync()).Phase);
        Assert.Empty(await check.Orders.ToListAsync()); Assert.Empty(await check.OrderPayments.ToListAsync());
    }

    [Theory]
    [InlineData("")] [InlineData("unknown-bank-date")] [InlineData("0001-01-01")]
    public async Task Exactly_bound_distinct_QR_with_only_unknown_bank_date_can_refund_after_trusted_observation_without_inventing_a_payment_time(string rawTime)
    {
        var session = await Web07BankSession.CreateAsync(fixture); var c = session.Case;
        session.Transport.Pay(session.Reference, 45_000m, bankTime: rawTime); await session.ProcessAsync();
        int receiptId; string protectedBefore; DateTime observed;
        await using (var db = c.Context())
        {
            var receipt = Assert.Single(await db.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId).ToListAsync());
            receiptId = receipt.Id; protectedBefore = receipt.ProtectedEvidence; observed = receipt.VerifiedAtUtc;
            Assert.Equal(BankReceiptAssetConfidence.CertifiedDistinct, receipt.AssetConfidence); Assert.Equal(BankTimePrecision.Unknown, receipt.TimePrecision);
            Assert.Equal(BankReceiptRoutingProvenance.BoundReference, receipt.RoutingProvenance); Assert.Null(receipt.OccurredAtUtc); Assert.Equal(rawTime, receipt.RawBankTime);
            Assert.Empty(await db.Set<StorefrontPaymentAllocation>().ToListAsync()); Assert.Equal(StorefrontPaymentState.Review, (await db.StorefrontOrders.SingleAsync()).PaymentState);
        }
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, true).CancelGuestAsync(await c.CancelRequestAsync());
        StorefrontRefundRead refund; await using (var db = c.Context()) refund = Assert.Single(await Web07Support.Refunds(c, db).GetAsync(c.OrderId));
        Assert.Equal(45_000m, refund.Amount); Assert.True(refund.CanApprove, refund.BlockedReason);
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ApproveAsync(c.OrderId, await c.RefundDecisionAsync(refund.Id, "Chỉ thiếu ngày ngân hàng; kiểm tra đầy đủ nguồn nguyên thủy và quan sát xác thực"));
        var debit = await Web07RecordedDebit.BankAsync(c, 45_000m); Assert.True(debit.ActualExecutionAt.UtcDateTime >= observed);
        var command = await c.ExecutionRequestAsync(refund.Id, debit.Source, c.Seed.LegalEntityId, c.Seed.BankAccountId, debit.ExternalReference, debit.ActualExecutionAt);
        var flowBefore = await Web07RefundExecutionTests.FlowAsync(c);
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ConfirmExecutionAsync(c.OrderId, command);
        Assert.Equal(flowBefore, await Web07RefundExecutionTests.FlowAsync(c));
        await using var check = c.Context(); var original = await check.Set<BankVerifiedReceipt>().SingleAsync(x => x.Id == receiptId);
        Assert.Equal(protectedBefore, original.ProtectedEvidence); Assert.Null(original.OccurredAtUtc); Assert.Equal(BankTimePrecision.Unknown, original.TimePrecision); Assert.True(original.NeedsReview);
        Assert.Equal(rawTime, original.RawBankTime); Assert.Equal(StorefrontPaymentState.Review, (await check.StorefrontOrders.SingleAsync()).PaymentState);
        Assert.Empty(await check.Set<StorefrontPaymentAllocation>().ToListAsync()); Assert.Empty(await check.OrderPayments.ToListAsync());
        Assert.Equal(45_000m, await check.Set<StorefrontRefundEvidence>().Where(x => x.RefundId == refund.Id).SumAsync(x => x.Amount));
        await Web05CashFlowTests.SnapshotIsolation(c.Seed);
        var report = await Web05CashFlowTests.Flow(c.Seed, DateTime.UtcNow.AddHours(7).Date);
        Assert.NotNull(report.BankReceiptReviews); Assert.Contains(report.BankReceiptReviews, x => x.Amount == 45_000m);
    }

    [Theory]
    [InlineData("same-id-amount-conflict")] [InlineData("reversed")] [InlineData("wrong-merchant")] [InlineData("future-time")]
    public async Task Independent_bank_conflict_or_binding_time_gate_cannot_be_promoted_to_refundable_by_a_mutable_date_review_label(string defect)
    {
        var session = await Web07BankSession.CreateAsync(fixture); var c = session.Case;
        var transaction = "W07-PROOF-" + Guid.NewGuid().ToString("N");
        if (defect == "wrong-merchant") session.Transport.MerchantOverride = "UNRELATED-MERCHANT";
        session.Transport.Pay(session.Reference, 45_000m, transaction,
            defect == "future-time" ? c.Clock.GetUtcNow().AddDays(2).ToString("O", CultureInfo.InvariantCulture) : null);
        await session.ProcessAsync();
        if (defect is "same-id-amount-conflict" or "reversed")
        {
            var order = session.Transport.Orders[session.Reference];
            lock (order.Transfers) order.Transfers[0] = order.Transfers[0] with { Amount = defect == "same-id-amount-conflict" ? 45_001m : 45_000m, Status = defect == "reversed" ? "REVERSED" : "COMPLETED" };
            await session.ProcessAsync();
        }
        await using (var db = c.Context())
        {
            Assert.Equal(45_000m, await db.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId).SumAsync(x => x.Amount));
            var denied = await Assert.ThrowsAsync<SqlException>(() => db.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId)
                .ExecuteUpdateAsync(x => x.SetProperty(r => r.ReviewCode, "BANK_TIME_UNKNOWN")));
            Assert.Contains(denied.Errors.Cast<SqlError>(), x => x.Number == 51163 && x.Message == "A financial review latch cannot be cleared or downgraded by replay.");
        }
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, true).CancelGuestAsync(await c.CancelRequestAsync());
        StorefrontRefundRead refund; await using (var db = c.Context()) refund = Assert.Single(await Web07Support.Refunds(c, db).GetAsync(c.OrderId));
        Assert.Equal(45_000m, refund.Amount); Assert.False(refund.CanApprove); Assert.False(refund.CanConfirmExecution); Assert.False(string.IsNullOrWhiteSpace(refund.BlockedReason));
        var command = await c.RefundDecisionAsync(refund.Id, "Một nhãn vận hành không xóa chứng cứ mâu thuẫn"); var before = await c.SnapshotAsync();
        await using (var db = c.Context()) await Assert.ThrowsAsync<ConflictAppException>(() => Web07Support.Refunds(c, db).ApproveAsync(c.OrderId, command));
        Assert.Equal(before, await c.SnapshotAsync());
        await using var check = c.Context(); Assert.Empty(await check.Set<StorefrontRefundEvidence>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
        Assert.Equal(StorefrontRefundState.Requested, (await check.Set<StorefrontRefund>().SingleAsync()).State);
    }
}

internal sealed record Web07BankSession(Web07Case Case, int IntentId, string Reference, Web05BankTransport Transport)
{
    internal static async Task<Web07BankSession> CreateAsync(Web01Fixture fixture)
    {
        var c = await Web07Case.CreateUnpaidQrAsync(fixture); int intent; string reference; string account;
        await using (var db = c.Context())
        {
            var row = await db.Set<StorefrontPaymentIntent>().SingleAsync(); intent = row.Id; reference = row.ProviderReference;
            account = (await db.StoreBankAccounts.SingleAsync(x => x.Id == c.Seed.BankAccountId)).AccountNumber;
        }
        var bank = new Web05BankTransport(account, c.Clock); await c.Banking!.ProcessAsync(intent, bank);
        return new(c, intent, reference, bank);
    }
    internal Task ProcessAsync(HttpMessageHandler? transport = null) => Case.Banking!.ProcessAsync(IntentId, transport ?? Transport);
    internal async Task CaptureDiagnosticsAsync(string name, Web07PagedTransport? transport = null)
    {
        await using var db = Case.Context();
        var state = await db.Set<StorefrontQrProcessing>().AsNoTracking().Where(x => x.PaymentIntentId == IntentId)
            .Select(x => new { x.State, x.Attempts, x.ReviewCode, x.NextAttemptAtUtc, x.LastAttemptAtUtc, x.NextScanBankDate, x.RequestedBankDate }).SingleAsync();
        var order = await db.StorefrontOrders.AsNoTracking().Where(x => x.Id == Case.OrderId).Select(x => new { x.Phase, x.PaymentState, x.Revision }).SingleAsync();
        var intent = await db.Set<StorefrontPaymentIntent>().AsNoTracking().Where(x => x.Id == IntentId).Select(x => new { x.CreatedAtUtc, x.ExpiresAtUtc, x.IsActive }).SingleAsync();
        await File.WriteAllTextAsync(Path.Combine(Case.Directory, "bank-diagnostic-" + name + ".json"), JsonSerializer.Serialize(new
        {
            ActualReadAtUtc = DateTime.UtcNow, ControlledClock = Case.Clock.GetUtcNow(), Case.StoreId, Case.OrderId, IntentId,
            State = state, Order = order, Intent = intent, TransportRequests = Transport.Requests.ToArray(),
            Counters = transport is null ? null : new { transport.PageOneCalls, transport.PageOneFailures, transport.CancellationCalls },
            Attempts = await db.Set<StorefrontBankScanAttempt>().AsNoTracking().Where(x => x.PaymentIntentId == IntentId)
                .Select(x => new { x.Id, x.StartedAtUtc, x.RequestedWindowsJson }).ToListAsync(),
            Completions = await db.Set<StorefrontBankScanCompletion>().AsNoTracking().Where(x => x.PaymentIntentId == IntentId)
                .Select(x => new { x.Id, x.ScanAttemptId, x.CompletedAtUtc, x.Fingerprint }).ToListAsync(),
            Observations = await db.Set<StorefrontBankObservation>().AsNoTracking().Where(x => x.PaymentIntentId == IntentId)
                .Select(x => new { x.Kind, x.Fingerprint, x.CreatedAtUtc }).ToListAsync(),
            NoRawTokenProtectedEvidenceOrAuthenticationHeaders = true, NoProofOrSchedulingMutation = true
        }));
    }
}
