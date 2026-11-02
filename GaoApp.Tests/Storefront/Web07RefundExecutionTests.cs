using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Services.Banking;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb07"), Trait("Category", "StorefrontWeb07")]
public sealed class Web07RefundExecutionTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Actual_bank_debit_accepts_an_opaque_nonASCII_external_reference_without_loss_or_SQL_UTF8_hash_disagreement()
    {
        var (c, refund) = await ApprovedAsync(); var external = "000-GIAO-DỊCH-đối-soát-" + Guid.NewGuid().ToString("N");
        var debit = await Web07RecordedDebit.BankAsync(c, 230_000m, external);
        var request = await c.ExecutionRequestAsync(refund.Id, debit.Source, c.Seed.LegalEntityId, c.Seed.BankAccountId, external, debit.ActualExecutionAt);
        var flow = await FlowAsync(c); await using (var db = c.Context()) await Web07Support.Refunds(c, db).ConfirmExecutionAsync(c.OrderId, request);
        Assert.Equal(flow, await FlowAsync(c)); await using var check = c.Context(); var evidence = Assert.Single(await check.Set<StorefrontRefundEvidence>().Where(x => x.RefundId == refund.Id).ToListAsync());
        using var snapshot = JsonDocument.Parse(evidence.SourceSnapshotJson!); Assert.Equal(external, snapshot.RootElement.GetProperty("externalTransactionId").GetString());
        var account = await check.StoreBankAccounts.SingleAsync(x => x.Id == c.Seed.BankAccountId);
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            "BANK-REFUND|" + GlobalBankReceiptLedger.AccountIdentity(account.BankCode, account.AccountNumber) + "|" + external)));
        Assert.Equal(expected, evidence.ExternalPayoutKey); Assert.Equal(230_000m, evidence.Amount); Assert.Single(await check.TreasuryEntries.ToListAsync());
    }

    [Fact]
    public async Task Rejection_after_a_real_partial_payout_preserves_paid_history_and_the_remaining_liability_until_authorized_reapproval()
    {
        var (c, refund) = await ApprovedAsync(); var first = await Web07RecordedDebit.BankAsync(c, 100_000m);
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ConfirmExecutionAsync(c.OrderId,
            await c.ExecutionRequestAsync(refund.Id, first.Source, c.Seed.LegalEntityId, c.Seed.BankAccountId, first.ExternalReference, first.ActualExecutionAt));
        await AssertGuestProgressAsync(c, StorefrontRefundState.Approved, 100_000m, 130_000m);
        var beforeDecision = await FlowAsync(c);
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).RejectAsync(c.OrderId, await c.RefundDecisionAsync(refund.Id, "Tạm chưa chấp thuận phần còn lại để đối soát; không xóa lần đã thực trả"));
        Assert.Equal(beforeDecision, await FlowAsync(c));
        await AssertGuestProgressAsync(c, StorefrontRefundState.Requested, 100_000m, 130_000m);
        await using (var db = c.Context())
        {
            var state = Assert.Single(await Web07Support.Refunds(c, db).GetAsync(c.OrderId));
            Assert.Equal(StorefrontRefundState.Rejected, state.State); Assert.Equal(230_000m, state.Amount); Assert.Equal(100_000m, state.ExecutedAmount);
            Assert.Equal(130_000m, state.Amount - state.ExecutedAmount); Assert.False(state.CanConfirmExecution); Assert.True(state.CanApprove);
            Assert.Single(state.Executions); Assert.Equal(230_000m, await db.Set<StorefrontRefundFunding>().Where(x => x.RefundId == refund.Id).SumAsync(x => x.Amount));
        }
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ApproveAsync(c.OrderId, await c.RefundDecisionAsync(refund.Id, "Đã đối soát, tiếp tục xử lý phần còn phải trả"));
        await AssertGuestProgressAsync(c, StorefrontRefundState.Approved, 100_000m, 130_000m);
        var second = await Web07RecordedDebit.BankAsync(c, 130_000m); var beforeClaim = await FlowAsync(c);
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ConfirmExecutionAsync(c.OrderId,
            await c.ExecutionRequestAsync(refund.Id, second.Source, c.Seed.LegalEntityId, c.Seed.BankAccountId, second.ExternalReference, second.ActualExecutionAt));
        Assert.Equal(beforeClaim, await FlowAsync(c)); await using var check = c.Context();
        Assert.Equal(230_000m, await check.Set<StorefrontRefundEvidence>().Where(x => x.RefundId == refund.Id).SumAsync(x => x.Amount));
        Assert.Equal(2, await check.Set<StorefrontRefundEvidence>().Where(x => x.RefundId == refund.Id).CountAsync());
        Assert.Equal(3, await check.Set<StorefrontRefundDecision>().Where(x => x.RefundId == refund.Id).CountAsync());
        Assert.Equal(StorefrontRefundState.Executed, (await check.Set<StorefrontRefund>().SingleAsync()).State);
        await AssertGuestProgressAsync(c, StorefrontRefundState.Executed, 230_000m, 0m);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Outgoing_external_transaction_uniqueness_uses_actual_physical_payer_identity_across_alias_rows_without_merging_different_accounts(bool physicalAlias)
    {
        var session = await Web07BankSession.CreateAsync(fixture); var c = session.Case;
        session.Transport.Pay(session.Reference, 45_000m); session.Transport.Pay(session.Reference, 1_000m); await session.ProcessAsync();
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, true).CancelGuestAsync(await c.CancelRequestAsync());
        StorefrontRefundRead[] refunds;
        await using (var db = c.Context()) refunds = (await Web07Support.Refunds(c, db).GetAsync(c.OrderId)).OrderByDescending(x => x.Amount).ToArray();
        Assert.Equal(new[] { 45_000m, 1_000m }, refunds.Select(x => x.Amount));
        foreach (var refund in refunds)
            await using (var db = c.Context()) await Web07Support.Refunds(c, db).ApproveAsync(c.OrderId, await c.RefundDecisionAsync(refund.Id, "Nguồn nhận phân biệt được giữ ngân sách riêng"));
        int secondPayer;
        await using (var db = c.Context())
        {
            var original = await db.StoreBankAccounts.SingleAsync(x => x.Id == c.Seed.BankAccountId);
            var alias = new StoreBankAccount { StoreId = c.StoreId, BankCode = original.BankCode, BankName = "WEB07 explicit actual payer account",
                AccountNumber = physicalAlias ? original.AccountNumber.Insert(3, " ") : "000OTHERPAYER" + c.StoreId, AccountName = original.AccountName, IsActive = true };
            db.Add(alias); await db.SaveChangesAsync(); secondPayer = alias.Id;
            Assert.Equal(physicalAlias, GlobalBankReceiptLedger.AccountIdentity(original.BankCode, original.AccountNumber) == GlobalBankReceiptLedger.AccountIdentity(alias.BankCode, alias.AccountNumber));
        }
        var external = "W07-EXACT-OUTGOING-" + Guid.NewGuid().ToString("N");
        var first = await Web07RecordedDebit.BankAsync(c, 45_000m, external);
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ConfirmExecutionAsync(c.OrderId,
            await c.ExecutionRequestAsync(refunds[0].Id, first.Source, c.Seed.LegalEntityId, c.Seed.BankAccountId, external, first.ActualExecutionAt));
        var second = await Web07RecordedDebit.BankAsync(c, 1_000m, external);
        var request = await c.ExecutionRequestAsync(refunds[1].Id, second.Source, c.Seed.LegalEntityId, secondPayer, external, second.ActualExecutionAt);
        var before = await c.SnapshotAsync(); var flow = await FlowAsync(c);
        if (physicalAlias)
        {
            await using var db = c.Context(); await Assert.ThrowsAsync<ConflictAppException>(() => Web07Support.Refunds(c, db).ConfirmExecutionAsync(c.OrderId, request));
            Assert.Equal(before, await c.SnapshotAsync());
        }
        else { await using var db = c.Context(); await Web07Support.Refunds(c, db).ConfirmExecutionAsync(c.OrderId, request); }
        Assert.Equal(flow, await FlowAsync(c)); await using var check = c.Context();
        Assert.Equal(physicalAlias ? 45_000m : 46_000m, await check.Set<StorefrontRefundEvidence>().Where(x => x.StorefrontOrderId == c.OrderId).SumAsync(x => x.Amount));
        Assert.Equal(46_000m, await check.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId).SumAsync(x => x.Amount));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_prepaid_QR_full_refund_claims_existing_cash_or_bank_debit_once_without_creating_another_outflow(bool cash)
    {
        var (c, refund) = await ApprovedAsync();
        var debit = cash ? await Web07RecordedDebit.CashAsync(c, 230_000m) : await Web07RecordedDebit.BankAsync(c, 230_000m);
        await using (var db = c.Context())
        {
            var sources = await Web07Support.Refunds(c, db).PayoutSourcesAsync(c.OrderId, refund.Id);
            Assert.Contains(sources, x => x.Kind == debit.Source.Kind && x.Id == debit.Source.Id && x.Amount == 230_000m);
        }
        var flowBefore = await FlowAsync(c);
        var command = await c.ExecutionRequestAsync(refund.Id, debit.Source, c.Seed.LegalEntityId,
            cash ? null : c.Seed.BankAccountId, debit.ExternalReference, cash ? null : debit.ActualExecutionAt);
        var revision = command.ExpectedRevision;
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ConfirmExecutionAsync(c.OrderId, command);
        Assert.Equal(flowBefore, await FlowAsync(c));
        await using (var db = c.Context())
        {
            var completed = Assert.Single(await Web07Support.Refunds(c, db).GetAsync(c.OrderId));
            Assert.Equal(StorefrontRefundState.Executed, completed.State); Assert.Equal(230_000m, completed.ExecutedAmount);
            var execution = Assert.Single(completed.Executions); Assert.Equal(230_000m, execution.Amount);
            Assert.Equal(debit.ActualExecutionAt.UtcDateTime, execution.ExecutedAtUtc);
            Assert.Equal(DateTimeKind.Utc, execution.ExecutedAtUtc.Kind);
            Assert.Equal(c.Seed.UserId, execution.ConfirmedByUserId);
            Assert.Equal(StorefrontOrderPhase.Cancelled, (await db.StorefrontOrders.SingleAsync(x => x.Id == c.OrderId)).Phase);
            Assert.True((await db.StorefrontOrders.SingleAsync(x => x.Id == c.OrderId)).Revision > revision);
            Assert.Empty(await db.Orders.ToListAsync()); Assert.Empty(await db.OrderPayments.ToListAsync());
            Assert.Equal(230_000m, await db.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId).SumAsync(x => x.Amount));
            Assert.Single(await db.Set<StorefrontRefundEvidence>().Where(x => x.RefundId == refund.Id).ToListAsync());
        }
        var after = await c.SnapshotAsync();
        await AssertGuestProgressAsync(c, StorefrontRefundState.Executed, 230_000m, 0m);
        await using (var restart = c.Context()) await Web07Support.Refunds(c, restart).ConfirmExecutionAsync(c.OrderId, command);
        Assert.Equal(after, await c.SnapshotAsync()); Assert.Equal(flowBefore, await FlowAsync(c));
    }

    [Fact]
    public async Task Two_real_smaller_debits_complete_one_refund_and_lost_response_retry_does_not_reuse_the_first_source()
    {
        var (c, refund) = await ApprovedAsync();
        var first = await Web07RecordedDebit.BankAsync(c, 100_000m);
        var command = await c.ExecutionRequestAsync(refund.Id, first.Source, c.Seed.LegalEntityId,
            c.Seed.BankAccountId, first.ExternalReference, first.ActualExecutionAt);
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ConfirmExecutionAsync(c.OrderId, command);
        await using (var db = c.Context())
        {
            var partial = Assert.Single(await Web07Support.Refunds(c, db).GetAsync(c.OrderId));
            Assert.Equal(230_000m, partial.Amount); Assert.Equal(100_000m, partial.ExecutedAmount);
            Assert.NotEqual(StorefrontRefundState.Executed, partial.State);
            Assert.Equal(130_000m, partial.Amount - partial.ExecutedAmount); Assert.Single(partial.Executions);
        }
        var afterPartial = await c.SnapshotAsync();
        await AssertGuestProgressAsync(c, StorefrontRefundState.Approved, 100_000m, 130_000m);
        await using (var restart = c.Context()) await Web07Support.Refunds(c, restart).ConfirmExecutionAsync(c.OrderId, command);
        Assert.Equal(afterPartial, await c.SnapshotAsync());
        var second = await Web07RecordedDebit.BankAsync(c, 130_000m);
        var finish = await c.ExecutionRequestAsync(refund.Id, second.Source, c.Seed.LegalEntityId,
            c.Seed.BankAccountId, second.ExternalReference, second.ActualExecutionAt);
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ConfirmExecutionAsync(c.OrderId, finish);
        await using var check = c.Context(); var completed = Assert.Single(await Web07Support.Refunds(c, check).GetAsync(c.OrderId));
        Assert.Equal(StorefrontRefundState.Executed, completed.State); Assert.Equal(230_000m, completed.ExecutedAmount);
        Assert.Equal(2, completed.Executions.Count); Assert.Equal(230_000m, completed.Executions.Sum(x => x.Amount));
        await AssertGuestProgressAsync(c, StorefrontRefundState.Executed, 230_000m, 0m);
        var flow = await Web05CashFlowTests.Flow(c.Seed, DateTime.UtcNow.AddHours(7).Date);
        Assert.Equal(230_000m, flow.Summary.Single(x => x.Fund == "bank").Inflow);
        Assert.Equal(230_000m, flow.Summary.Single(x => x.Fund == "bank").Outflow);
    }

    [Fact]
    public async Task Oversized_actual_debit_is_rejected_whole_without_clamping_or_consuming_its_leftover()
    {
        var (c, refund) = await ApprovedAsync(); var debit = await Web07RecordedDebit.BankAsync(c, 230_001m);
        var command = await c.ExecutionRequestAsync(refund.Id, debit.Source, c.Seed.LegalEntityId,
            c.Seed.BankAccountId, debit.ExternalReference, debit.ActualExecutionAt);
        var before = await c.SnapshotAsync();
        await using (var db = c.Context()) await Assert.ThrowsAsync<ConflictAppException>(() =>
            Web07Support.Refunds(c, db).ConfirmExecutionAsync(c.OrderId, command));
        Assert.Equal(before, await c.SnapshotAsync());
        await using var check = c.Context(); var remaining = Assert.Single(await Web07Support.Refunds(c, check).GetAsync(c.OrderId));
        Assert.Equal(230_000m, remaining.Amount); Assert.Equal(0m, remaining.ExecutedAmount);
        Assert.Empty(remaining.Executions); Assert.Equal(-230_001m, (await check.TreasuryEntries.SingleAsync()).Amount);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Partial_CashOut_and_its_real_Treasury_voucher_are_one_payout_even_while_more_refund_budget_remains(bool voucherFirst)
    {
        var (c, refund) = await ApprovedAsync(); var cash = await Web07RecordedDebit.CashAsync(c, 100_000m);
        var voucher = await Web07RecordedDebit.CashVoucherAsync(c, cash);
        var first = voucherFirst ? voucher : cash.Source; var other = voucherFirst ? cash.Source : voucher;
        var command = await c.ExecutionRequestAsync(refund.Id, first, c.Seed.LegalEntityId, null, cash.ExternalReference, null);
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ConfirmExecutionAsync(c.OrderId, command);
        await using (var db = c.Context())
        {
            var partial = Assert.Single(await Web07Support.Refunds(c, db).GetAsync(c.OrderId));
            Assert.Equal(100_000m, partial.ExecutedAmount); Assert.Equal(130_000m, partial.Amount - partial.ExecutedAmount);
        }
        var repeatSource = await c.ExecutionRequestAsync(refund.Id, other, c.Seed.LegalEntityId, null, cash.ExternalReference, null);
        var before = await c.SnapshotAsync();
        await using (var db = c.Context()) await Assert.ThrowsAsync<ConflictAppException>(() =>
            Web07Support.Refunds(c, db).ConfirmExecutionAsync(c.OrderId, repeatSource));
        Assert.Equal(before, await c.SnapshotAsync());
        var flow = await Web05CashFlowTests.Flow(c.Seed, DateTime.UtcNow.AddHours(7).Date);
        Assert.Equal(100_000m, flow.Summary.Single(x => x.Fund == "cash").Outflow);
        await using var check = c.Context(); Assert.Equal(100_000m,
            await check.Set<StorefrontRefundEvidence>().Where(x => x.RefundId == refund.Id).SumAsync(x => x.Amount));
    }

    [Fact]
    public async Task Checkout_namespace_can_reuse_one_GUID_for_guest_cancel_refund_approval_and_execution_without_cross_operation_receipt_collision()
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture, prepaidQr: true); Guid key; StorefrontPlaceOrderRequest placement;
        await using (var db = c.Context())
        {
            key = await db.Set<StorefrontCommandReceipt>().Where(x => x.StorefrontOrderId == c.OrderId).OrderBy(x => x.Id)
                .Select(x => x.ClientRequestId).FirstAsync();
            var order = await db.StorefrontOrders.SingleAsync(x => x.Id == c.OrderId);
            var quote = await db.Set<StorefrontQuote>().SingleAsync(x => x.Id == order.QuoteId);
            placement = new() { ClientRequestId = key, QuoteToken = quote.Token, Fingerprint = quote.Fingerprint };
        }
        var cancel = await c.CancelRequestAsync(key: key);
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, guest: true).CancelGuestAsync(cancel);
        StorefrontRefundRead refund; await using (var db = c.Context()) refund = Assert.Single(await Web07Support.Refunds(c, db).GetAsync(c.OrderId));
        var approve = await c.RefundDecisionAsync(refund.Id, "Nguồn QR đã kiểm tra", key);
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ApproveAsync(c.OrderId, approve);
        var debit = await Web07RecordedDebit.BankAsync(c, 230_000m);
        var execution = await c.ExecutionRequestAsync(refund.Id, debit.Source, c.Seed.LegalEntityId,
            c.Seed.BankAccountId, debit.ExternalReference, debit.ActualExecutionAt, key);
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ConfirmExecutionAsync(c.OrderId, execution);
        var after = await c.SnapshotAsync();
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, guest: true).CancelGuestAsync(cancel);
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ApproveAsync(c.OrderId, approve);
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ConfirmExecutionAsync(c.OrderId, execution);
        var checkoutReplay = await c.Banking!.PlaceAsync(placement);
        Assert.Equal(StorefrontPlacementStatus.Placed, checkoutReplay.Status); Assert.NotNull(checkoutReplay.Order);
        Assert.Equal(c.Placed.Code, checkoutReplay.Order.Code);
        Assert.Equal(after, await c.SnapshotAsync());
        await using var check = c.Context(); var receipts = await check.Set<StorefrontCommandReceipt>()
            .Where(x => x.StorefrontOrderId == c.OrderId && x.ClientRequestId == key).ToListAsync();
        Assert.Equal(4, receipts.Count); Assert.Equal(4, receipts.Select(x => x.Operation).Distinct().Count());
        Assert.Single(await check.Set<StorefrontRefund>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
        Assert.Equal(230_000m, Assert.Single(await Web07Support.Refunds(c, check).GetAsync(c.OrderId)).ExecutedAmount);
    }

    [Fact]
    public async Task Reject_does_not_waive_paid_cancellation_liability_and_reapproval_appends_history_without_duplicate_obligation()
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture, prepaidQr: true);
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, guest: true).CancelGuestAsync(await c.CancelRequestAsync());
        StorefrontRefundRead refund; await using (var db = c.Context()) refund = Assert.Single(await Web07Support.Refunds(c, db).GetAsync(c.OrderId));
        var reject = await c.RefundDecisionAsync(refund.Id, "Chờ bổ sung thông tin thực trả; không xóa nghĩa vụ230000");
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).RejectAsync(c.OrderId, reject);
        await using (var db = c.Context())
        {
            var rejected = Assert.Single(await Web07Support.Refunds(c, db).GetAsync(c.OrderId));
            Assert.Equal(StorefrontRefundState.Rejected, rejected.State); Assert.Equal(230_000m, rejected.Amount);
            Assert.Equal(0m, rejected.ExecutedAmount); Assert.False(rejected.CanConfirmExecution);
            Assert.False(string.IsNullOrWhiteSpace(rejected.BlockedReason));
            Assert.Equal(230_000m, await db.Set<StorefrontRefundFunding>().Where(x => x.RefundId == refund.Id).SumAsync(x => x.Amount));
        }
        var approve = await c.RefundDecisionAsync(refund.Id, "Đã bổ sung chứng cứ, duyệt lại nghĩa vụ cũ");
        await AssertGuestProgressAsync(c, StorefrontRefundState.Requested, 0m, 230_000m);
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ApproveAsync(c.OrderId, approve);
        await using var check = c.Context();
        Assert.Single(await check.Set<StorefrontRefund>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
        Assert.Single(await check.Set<StorefrontRefundFunding>().Where(x => x.RefundId == refund.Id).ToListAsync());
        var history = await check.Set<StorefrontRefundDecision>().Where(x => x.RefundId == refund.Id).ToListAsync();
        Assert.Equal(2, history.Count); Assert.Contains(history, x => x.Kind == StorefrontRefundDecisionKind.Rejected);
        Assert.Contains(history, x => x.Kind == StorefrontRefundDecisionKind.Approved);
        var current = Assert.Single(await Web07Support.Refunds(c, check).GetAsync(c.OrderId));
        Assert.Equal(StorefrontRefundState.Approved, current.State); Assert.Equal(230_000m, current.Amount); Assert.Equal(0m, current.ExecutedAmount);
        await AssertGuestProgressAsync(c, StorefrontRefundState.Approved, 0m, 230_000m);
    }

    private static async Task AssertGuestProgressAsync(Web07Case c, StorefrontRefundState expected, decimal executed, decimal remaining)
    {
        await using var db = c.Context();
        var view = await Web07Support.Cancellation(c, db, guest: true).GetGuestAsync(c.Token);
        Assert.NotNull(view); Assert.Equal(StorefrontOrderPhase.Cancelled, view.Phase);
        Assert.Equal(expected, view.RefundState);
        Assert.Equal(expected, (await db.StorefrontOrders.AsNoTracking().SingleAsync(x => x.Id == c.OrderId)).RefundState);
        Assert.Equal(230_000m, view.Refunds.Sum(x => x.Amount));
        Assert.Equal(executed, view.Refunds.Sum(x => x.ExecutedAmount));
        Assert.Equal(remaining, view.Refunds.Sum(x => x.Amount - x.ExecutedAmount));
    }

    private async Task<(Web07Case Case, StorefrontRefundRead Refund)> ApprovedAsync()
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture, prepaidQr: true);
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, guest: true).CancelGuestAsync(await c.CancelRequestAsync());
        StorefrontRefundRead refund; await using (var db = c.Context()) refund = Assert.Single(await Web07Support.Refunds(c, db).GetAsync(c.OrderId));
        Assert.Equal(230_000m, refund.Amount); Assert.Equal(0m, refund.ExecutedAmount); Assert.Equal(StorefrontRefundState.Requested, refund.State);
        await AssertGuestProgressAsync(c, StorefrontRefundState.Requested, 0m, 230_000m);
        var approve = await c.RefundDecisionAsync(refund.Id, "Xác minh đầy đủ nguồn QR và số tiền hàng/phí giao khách đã trả");
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ApproveAsync(c.OrderId, approve);
        await using (var db = c.Context())
        {
            var approved = Assert.Single(await Web07Support.Refunds(c, db).GetAsync(c.OrderId));
            Assert.Equal(StorefrontRefundState.Approved, approved.State); Assert.Equal(0m, approved.ExecutedAmount);
            Assert.Empty(approved.Executions); Assert.Empty(await db.TreasuryEntries.ToListAsync());
            Assert.Empty(await db.POSShiftCashTransactions.ToListAsync());
            Assert.True((await db.StorefrontOrders.SingleAsync(x => x.Id == c.OrderId)).Revision > approve.ExpectedRevision);
            refund = approved;
        }
        await AssertGuestProgressAsync(c, StorefrontRefundState.Approved, 0m, 230_000m);
        await Web05CashFlowTests.SnapshotIsolation(c.Seed); return (c, refund);
    }
    internal static async Task<string> FlowAsync(Web07Case c)
    {
        var flow = await Web05CashFlowTests.Flow(c.Seed, DateTime.UtcNow.AddHours(7).Date);
        return JsonSerializer.Serialize(flow.Summary.OrderBy(x => x.Fund).Select(x => new { x.Fund, x.Inflow, x.Outflow }));
    }
}
