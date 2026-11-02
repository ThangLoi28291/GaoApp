using GaoApp.Application.Services.Reports;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Services.Reports;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb07"), Trait("Category", "StorefrontWeb07")]
public sealed class Web07CashFlowTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Confirmed_native_Treasury_payout_uses_actual_attested_time_on_the_same_one_TC_flow_and_hour_bucket_without_new_expense_or_outflow()
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture, prepaidQr: true);
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, true).CancelGuestAsync(await c.CancelRequestAsync());
        int refund; await using (var db = c.Context()) refund = (await db.Set<StorefrontRefund>().SingleAsync()).Id;
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ApproveAsync(c.OrderId, await c.RefundDecisionAsync(refund, "Đủ chứng cứ nguồn nhận để đối soát lần thực chi"));
        var debit = await Web07RecordedDebit.BankAsync(c, 230_000m); var day = debit.ActualExecutionAt.ToOffset(TimeSpan.FromHours(7)).Date;
        var before = await Web05CashFlowTests.Flow(c.Seed, day); var prior = Assert.Single(before.Items, x => x.Id == "TC-" + debit.Source.Id);
        Assert.Equal(-230_000m, prior.Amount); Assert.Equal(debit.Source.SourceAtUtc, prior.AtUtc);
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ConfirmExecutionAsync(c.OrderId,
            await c.ExecutionRequestAsync(refund, debit.Source, c.Seed.LegalEntityId, c.Seed.BankAccountId, debit.ExternalReference, debit.ActualExecutionAt));
        var after = await Web05CashFlowTests.Flow(c.Seed, day); var actual = Assert.Single(after.Items, x => x.Id == prior.Id);
        Assert.Equal(prior.Id, actual.Id); Assert.Equal(prior.Amount, actual.Amount); Assert.Equal(debit.ActualExecutionAt.UtcDateTime, actual.AtUtc); Assert.Equal(DateTimeKind.Utc, actual.AtUtc.Kind);
        Assert.Equal(before.Items.Count, after.Items.Count); Assert.Equal(before.Summary.Single(x => x.Fund == "bank").Inflow, after.Summary.Single(x => x.Fund == "bank").Inflow);
        Assert.Equal(230_000m, after.Summary.Single(x => x.Fund == "bank").Outflow); Assert.Equal(before.Summary.Single(x => x.Fund == "bank").Outflow, after.Summary.Single(x => x.Fund == "bank").Outflow);
        Assert.False(actual.CanReverse); Assert.Equal(1, after.Period.BucketHours);
        var correctBucket = (int)((actual.AtUtc - after.Period.FromUtc).TotalHours / after.Period.BucketHours);
        Assert.InRange(correctBucket, 0, after.Trend.Count - 1); Assert.Equal(230_000m, after.Trend[correctBucket].Outflow);
        var oldBucket = (int)((prior.AtUtc - after.Period.FromUtc).TotalHours / after.Period.BucketHours);
        if (oldBucket != correctBucket) Assert.Equal(0m, after.Trend[oldBucket].Outflow);
        // Same query/API filters retain one full business-day outflow. The
        // timestamp bucket changed from a book-day anchor, not a second event.
        await using var check = c.Context(); Assert.Single(await check.TreasuryEntries.ToListAsync()); Assert.Empty(await check.POSShiftCashTransactions.ToListAsync());
        Assert.Empty(await check.OperatingExpenses.ToListAsync()); Assert.Empty(await check.Orders.ToListAsync()); Assert.Empty(await check.OrderPayments.ToListAsync());
        Assert.Equal(230_000m, Assert.Single(await check.Set<StorefrontRefundEvidence>().Where(x => x.RefundId == refund).ToListAsync()).Amount);
        var reportAgain = await new OperationsReportService(check, new SalesReportingPeriodPolicy()).CashFlowAsync(new() { FromDate = day, ToDate = day, Fund = "bank" }, default);
        Assert.Equal(actual.AtUtc, Assert.Single(reportAgain.Items, x => x.Id == prior.Id).AtUtc);
    }
}
