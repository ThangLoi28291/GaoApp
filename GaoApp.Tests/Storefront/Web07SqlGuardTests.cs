using GaoApp.Application.Storefront;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Services.Banking;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb07"), Trait("Category", "StorefrontWeb07")]
public sealed class Web07SqlGuardTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData("[]", 547)] [InlineData("{}", 51300)]
    [InlineData("[{}]", 547)] [InlineData("[{},{}]", 547)]
    public async Task Raw_native_execution_cannot_turn_a_real_CashIn_into_a_payout_by_exploiting_an_empty_or_nonobject_source_snapshot(string snapshot, int expectedError)
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture, prepaidQr: true);
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, true).CancelGuestAsync(await c.CancelRequestAsync());
        int refund; int funding;
        await using (var db = c.Context()) { var row = await db.Set<StorefrontRefundFunding>().SingleAsync(); funding = row.Id; refund = row.RefundId; }
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ApproveAsync(c.OrderId, await c.RefundDecisionAsync(refund, "Xác minh nguồn nhận; chưa có khoản thực trả nào"));
        var terminal = await Web06FinanceSupport.OwnOpenShiftAsync(c.Physical!);
        var actualCashIn = await Web06FinanceSupport.ActualCashInAsync(c.Physical!, terminal, 230_000m);
        DateTime at; await using (var db = c.Context()) at = (await db.POSShiftCashTransactions.SingleAsync(x => x.Id == actualCashIn)).CreatedAtUtc;
        const string evidence = "WEB07 explicit invalid SQL forgery attempt, not actual payout proof";
        var key = $"CASH:{c.StoreId}:{actualCashIn}"; var fingerprint = GlobalBankReceiptLedger.Hash(snapshot + "|" + evidence);
        var protectedContext = Web05Support.AdminProtocol(c.Banking!.AdminKeys).Protect(c.StoreId, "WEB07 negative-only forged claim context; must not be accepted");
        var before = await c.SnapshotAsync(); var flowBefore = await Web07RefundExecutionTests.FlowAsync(c); SqlException? failure = null;
        await using (var db = c.Context())
        {
            try
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT StorefrontRefundEvidence(StoreId,RefundId,StorefrontOrderId,FundingId,PayoutSourceKind,POSShiftCashTransactionId,PayerLegalEntityId,Fund,VerificationMethod,EvidenceReference,ProtectedPayoutEvidence,SourceSnapshotJson,ExternalPayoutKey,EvidenceFingerprint,Amount,ExecutedAtUtc,ConfirmedByUserId,CreatedAtUtc,IsDeleted) VALUES({c.StoreId},{refund},{c.OrderId},{funding},1,{actualCashIn},{c.Seed.LegalEntityId},N'cash',N'STAFF_CONFIRMED_CASH_OUT',{evidence},{protectedContext},{snapshot},{key},{fingerprint},230000,{at},{c.Seed.UserId},SYSUTCDATETIME(),0)");
            }
            catch (SqlException error) { failure = error; }
        }
        var after = await c.SnapshotAsync(); var flowAfter = await Web07RefundExecutionTests.FlowAsync(c);
        await using (var db = c.Context()) await File.WriteAllTextAsync(Path.Combine(c.Directory, "cashin-forgery-" + GlobalBankReceiptLedger.Hash(snapshot)[..12] + ".json"), JsonSerializer.Serialize(new
        {
            Attempt = snapshot, SqlRejected = failure is not null, SqlNumber = failure?.Number,
            ActualCashInId = actualCashIn, c.OrderId, c.StoreId, EvidenceRows = await db.Set<StorefrontRefundEvidence>().Where(x => x.RefundId == refund).CountAsync(),
            CashOutRows = await db.POSShiftCashTransactions.CountAsync(x => x.Type == GaoApp.Domain.Enums.POSShiftCashTransactionType.CashOut),
            BeforeAfterFinancialStateEqual = before == after, BeforeAfterCashflowEqual = flowBefore == flowAfter,
            GenuineCancelledAndApproved = true, NoActualPayoutSeeded = true
        }));
        Assert.NotNull(failure); Assert.Equal(expectedError, failure.Number);
        if (expectedError == 547) Assert.Contains(failure.Errors.Cast<SqlError>(), x => x.Number == 547 && x.Message.Contains("\"CK_StorefrontRefundEvidence_NativePayout\"", StringComparison.Ordinal));
        Assert.Equal(before, after); Assert.Equal(flowBefore, flowAfter);
    }

    [Theory]
    [InlineData("request-update", 51300)] [InlineData("request-delete", 51300)]
    [InlineData("funding-update", 51300)] [InlineData("refund-decision-delete", 51300)]
    [InlineData("obligation-reprice", 51300)] [InlineData("obligation-delete", 547)]
    [InlineData("execution-update", 51090)] [InlineData("execution-delete", 51090)]
    [InlineData("treasury-amount", 51300)] [InlineData("treasury-fund", 51300)] [InlineData("treasury-transfer", 51300)] [InlineData("treasury-reverse", 51300)]
    [InlineData("cash-amount", 51300)] [InlineData("cash-time", 51300)]
    [InlineData("scan-start", 51300)] [InlineData("scan-completion", 51300)]
    [InlineData("reopen-order", 51300)] [InlineData("pretend-executed", 51300)]
    public async Task Raw_SQL_cannot_rewrite_real_cancellation_source_obligation_payout_or_its_actual_cash_treasury_facts(string mutation, int expectedNumber)
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture, prepaidQr: true);
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, true).CancelGuestAsync(await c.CancelRequestAsync());
        int refund; await using (var db = c.Context()) refund = (await db.Set<StorefrontRefund>().SingleAsync()).Id;
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ApproveAsync(c.OrderId, await c.RefundDecisionAsync(refund, "WEB07 actual source/authority checked"));
        Web07RecordedDebit? debit = null;
        if (mutation != "pretend-executed")
        {
            var cash = mutation.StartsWith("cash-", StringComparison.Ordinal);
            debit = cash ? await Web07RecordedDebit.CashAsync(c, 230_000m) : await Web07RecordedDebit.BankAsync(c, 230_000m);
            await using var db = c.Context(); await Web07Support.Refunds(c, db).ConfirmExecutionAsync(c.OrderId,
                await c.ExecutionRequestAsync(refund, debit.Source, c.Seed.LegalEntityId, cash ? null : c.Seed.BankAccountId, debit.ExternalReference, cash ? null : debit.ActualExecutionAt));
        }
        var before = await c.SnapshotAsync(); var flow = await Web07RefundExecutionTests.FlowAsync(c);
        var sql = mutation switch
        {
            "request-update" => "UPDATE StorefrontCancellationRequests SET Reason=N'changed history' WHERE StoreId={0} AND StorefrontOrderId={1}",
            "request-delete" => "DELETE StorefrontCancellationRequests WHERE StoreId={0} AND StorefrontOrderId={1}",
            "funding-update" => "UPDATE StorefrontRefundFundings SET SourceSnapshotJson=N'{{\"tampered\":true}}' WHERE StoreId={0} AND StorefrontOrderId={1}",
            "refund-decision-delete" => "DELETE StorefrontRefundDecisions WHERE StoreId={0} AND StorefrontOrderId={1}",
            "obligation-reprice" => "UPDATE StorefrontRefunds SET GoodsAmount=GoodsAmount-1,Amount=Amount-1 WHERE StoreId={0} AND StorefrontOrderId={1}",
            "obligation-delete" => "DELETE StorefrontRefunds WHERE StoreId={0} AND StorefrontOrderId={1}",
            "execution-update" => "UPDATE StorefrontRefundEvidence SET EvidenceReference=N'changed actual payout evidence' WHERE StoreId={0} AND StorefrontOrderId={1}",
            "execution-delete" => "DELETE StorefrontRefundEvidence WHERE StoreId={0} AND StorefrontOrderId={1}",
            "treasury-amount" => "UPDATE TreasuryEntries SET Amount=Amount-1 WHERE StoreId={0} AND Id={2}",
            "treasury-fund" => "UPDATE TreasuryEntries SET Fund=N'cash' WHERE StoreId={0} AND Id={2}",
            "treasury-transfer" => "UPDATE TreasuryEntries SET TargetFund=N'cash' WHERE StoreId={0} AND Id={2}",
            "treasury-reverse" => "UPDATE TreasuryEntries SET ReversedAtUtc=SYSUTCDATETIME() WHERE StoreId={0} AND Id={2}",
            "cash-amount" => "UPDATE POSShiftCashTransactions SET Amount=Amount+1 WHERE StoreId={0} AND Id={2}",
            "cash-time" => "UPDATE POSShiftCashTransactions SET CreatedAtUtc=DATEADD(second,1,CreatedAtUtc) WHERE StoreId={0} AND Id={2}",
            "scan-start" => "UPDATE StorefrontBankScanAttempts SET BindingFingerprint=REPLICATE(N'F',64) WHERE StoreId={0} AND StorefrontOrderId={1}",
            "scan-completion" => "UPDATE StorefrontBankScanCompletions SET Fingerprint=REPLICATE(N'E',64) WHERE StoreId={0} AND StorefrontOrderId={1}",
            "reopen-order" => "UPDATE StorefrontOrders SET Phase=1 WHERE StoreId={0} AND Id={1}",
            _ => "UPDATE StorefrontRefunds SET State=3 WHERE StoreId={0} AND StorefrontOrderId={1}"
        };
        await using (var db = c.Context())
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql, c.StoreId, c.OrderId, debit?.Source.Id ?? 0));
            Assert.Equal(expectedNumber, error.Number);
        }
        Assert.Equal(before, await c.SnapshotAsync()); Assert.Equal(flow, await Web07RefundExecutionTests.FlowAsync(c));
    }

    [Fact]
    public async Task Claimed_actual_bank_debit_cannot_be_reversed_via_the_real_existing_Treasury_service()
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture, prepaidQr: true);
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, true).CancelGuestAsync(await c.CancelRequestAsync());
        int refund; await using (var db = c.Context()) refund = (await db.Set<StorefrontRefund>().SingleAsync()).Id;
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ApproveAsync(c.OrderId, await c.RefundDecisionAsync(refund, "Xác minh nguồn trước thực hoàn"));
        var debit = await Web07RecordedDebit.BankAsync(c, 230_000m);
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ConfirmExecutionAsync(c.OrderId,
            await c.ExecutionRequestAsync(refund, debit.Source, c.Seed.LegalEntityId, c.Seed.BankAccountId, debit.ExternalReference, debit.ActualExecutionAt));
        var before = await c.SnapshotAsync();
        await using (var db = c.Context())
        {
            var error = await Assert.ThrowsAsync<GaoApp.Application.Common.Exceptions.ConflictAppException>(() => new GaoApp.Infrastructure.Services.Reports.TreasuryService(db).ReverseAsync(debit.Source.Id,
                new() { ClientRequestId = Guid.NewGuid(), RowVersion = debit.Source.Version, Date = DateTime.UtcNow.AddHours(7).Date, Reason = "Không được đảo riêng nguồn đã thực hoàn" }, default));
            Assert.Equal("Phiếu chi đã là chứng cứ thực hoàn website; không đảo riêng nguồn đã xác nhận.", error.Message);
        }
        Assert.Equal(before, await c.SnapshotAsync());
    }

    [Theory]
    [InlineData("Thu công nợ khách hàng")] [InlineData("Nhận cọc khách hàng")] [InlineData("Hoàn cọc khách hàng")]
    public async Task An_actual_claimed_cash_out_cannot_be_relabelled_as_reserved_legacy_money_after_native_execution(string protectedReason)
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture, prepaidQr: true);
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, true).CancelGuestAsync(await c.CancelRequestAsync());
        int refund; await using (var db = c.Context()) refund = (await db.Set<StorefrontRefund>().SingleAsync()).Id;
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ApproveAsync(c.OrderId, await c.RefundDecisionAsync(refund, "Nguồn tiền và khoản chi tiền mặt thực tế đã được kiểm tra"));
        var actual = await Web07RecordedDebit.CashAsync(c, 230_000m);
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ConfirmExecutionAsync(c.OrderId,
            await c.ExecutionRequestAsync(refund, actual.Source, c.Seed.LegalEntityId, null, actual.ExternalReference, actual.ActualExecutionAt));
        var before = await c.SnapshotAsync(); var flow = await Web07RefundExecutionTests.FlowAsync(c);
        await using (var db = c.Context())
        {
            var denied = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE POSShiftCashTransactions SET Reason={protectedReason} WHERE StoreId={c.StoreId} AND Id={actual.Source.Id}"));
            Assert.Equal(51300, denied.Number);
        }
        Assert.Equal(before, await c.SnapshotAsync()); Assert.Equal(flow, await Web07RefundExecutionTests.FlowAsync(c));
    }
}
