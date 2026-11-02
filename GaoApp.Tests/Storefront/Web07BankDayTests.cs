using GaoApp.Domain.Entities;
using System.Globalization;
using System.Reflection;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Services.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb07"), Trait("Category", "StorefrontWeb07")]
public sealed class Web07BankDayTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Genuine_day_precision_uses_earlier_trusted_observation_or_exclusive_original_bank_day_end_without_timestamp_fabrication(bool delayedObservation)
    {
        var session = await Web07BankSession.CreateAsync(fixture); var c = session.Case;
        var day = c.Clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).Date;
        var dayStart = DateTime.SpecifyKind(day.AddHours(-7), DateTimeKind.Utc); var dayEnd = dayStart.AddDays(1);
        c.SynchronizeCommandClockWithActualLegacyTime = false;
        if (delayedObservation) c.Checkout.Clock.Utc = dayEnd.AddMinutes(10);
        session.Transport.Pay(session.Reference, 45_000m, bankTime: day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        await session.ProcessAsync();
        int globalId; string protectedBefore; DateTime observed;
        await using (var db = c.Context())
        {
            var row = Assert.Single(await db.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId).ToListAsync());
            globalId = row.Id; protectedBefore = row.ProtectedEvidence; observed = row.VerifiedAtUtc;
            Assert.Equal(BankTimePrecision.BankDay, row.TimePrecision); Assert.Equal(day, row.BankBusinessDate); Assert.Equal(dayStart, row.OccurredAtUtc);
        }
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, true).CancelGuestAsync(await c.CancelRequestAsync());
        int refund;
        await using (var db = c.Context())
        {
            var source = await db.Set<StorefrontRefundFunding>().SingleAsync(x => x.StorefrontOrderId == c.OrderId); refund = source.RefundId;
            var order = await db.StorefrontOrders.AsNoTracking().SingleAsync(x => x.Id == c.OrderId);
            // Read-only observation of the internal proof result, never a factory,
            // mutation, replacement policy, or financial positive arrangement.
            var type = typeof(StorefrontCancellationService).Assembly.GetType("GaoApp.Infrastructure.Services.Storefront.StorefrontRefundSourceProof", throwOnError: true)!;
            var method = type.GetMethod("ReadAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
            var task = (Task)method.Invoke(null, [db, Web05Support.AdminProtocol(c.Banking!.AdminKeys), order, source, CancellationToken.None])!;
            await task; var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
            Assert.True((bool)result.GetType().GetProperty("Eligible")!.GetValue(result)!, (string?)result.GetType().GetProperty("BlockedReason")!.GetValue(result));
            Assert.Equal(observed < dayEnd ? observed : dayEnd, (DateTime)result.GetType().GetProperty("EarliestPayoutAtUtc")!.GetValue(result)!);
        }
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ApproveAsync(c.OrderId, await c.RefundDecisionAsync(refund, "Giữ nguyên độ chính xác ngày, không gán giờ thanh toán"));
        var debit = await Web07RecordedDebit.BankAsync(c, 45_000m);
        if (delayedObservation) c.Checkout.Clock.Utc = dayEnd.AddMinutes(10);
        var request = await c.ExecutionRequestAsync(refund, debit.Source, c.Seed.LegalEntityId, c.Seed.BankAccountId, debit.ExternalReference, debit.ActualExecutionAt);
        if (delayedObservation)
        {
            // Real current-time debit is before this forward-controlled day's safe
            // end. Do not pretend a historical/future payout was actually executed.
            Assert.True(debit.ActualExecutionAt.UtcDateTime < dayEnd); var before = await c.SnapshotAsync();
            await using var db = c.Context(); await Assert.ThrowsAsync<ConflictAppException>(() => Web07Support.Refunds(c, db).ConfirmExecutionAsync(c.OrderId, request));
            Assert.Equal(before, await c.SnapshotAsync());
        }
        else
        {
            var flow = await Web07RefundExecutionTests.FlowAsync(c);
            await using (var db = c.Context()) await Web07Support.Refunds(c, db).ConfirmExecutionAsync(c.OrderId, request);
            Assert.Equal(flow, await Web07RefundExecutionTests.FlowAsync(c));
        }
        await using var check = c.Context(); var original = await check.Set<BankVerifiedReceipt>().SingleAsync(x => x.Id == globalId);
        Assert.Equal(protectedBefore, original.ProtectedEvidence); Assert.Equal(day, original.BankBusinessDate); Assert.Equal(dayStart, original.OccurredAtUtc); Assert.Equal(BankTimePrecision.BankDay, original.TimePrecision);
        Assert.Equal(delayedObservation ? 0m : 45_000m, await check.Set<StorefrontRefundEvidence>().Where(x => x.RefundId == refund).SumAsync(x => x.Amount));
    }
}
