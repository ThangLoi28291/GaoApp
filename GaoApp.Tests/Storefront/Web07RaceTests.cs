using GaoApp.Domain.Entities;
using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb07"), Trait("Category", "StorefrontWeb07")]
public sealed class Web07RaceTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task Cancellation_and_real_preparation_or_dispatch_serialize_on_actual_proof_order_locks_and_never_commit_partial_physical_effects(
        bool dispatch, bool cancellationFirst)
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture); var physical = c.Physical!;
        if (dispatch) { await physical.PrepareAsync(); await physical.PickAllAsync(); }
        var cancel = await c.CancelRequestAsync();
        StorefrontOperationRequest physicalCommand = dispatch ? await physical.DispatchRequestAsync() : await c.CommandAsync<StorefrontOperationRequest>();
        using var barrier = new Web07OrderReadBarrier(); await using var firstDb = c.Context(barrier); await using var secondDb = c.Context();
        await secondDb.Database.OpenConnectionAsync(); var secondSpid = await secondDb.Database.SqlQueryRaw<int>("SELECT CONVERT(int,@@SPID) AS [Value]").SingleAsync();
        Task First() => cancellationFirst ? Web07Support.Cancellation(c, firstDb, true).CancelGuestAsync(cancel)
            : dispatch ? physical.Service(firstDb).DispatchAsync(c.OrderId, (StorefrontDispatchRequest)physicalCommand)
            : physical.Service(firstDb).StartPreparationAsync(c.OrderId, physicalCommand);
        Task Second() => !cancellationFirst ? Web07Support.Cancellation(c, secondDb, true).CancelGuestAsync(cancel)
            : dispatch ? physical.Service(secondDb).DispatchAsync(c.OrderId, (StorefrontDispatchRequest)physicalCommand)
            : physical.Service(secondDb).StartPreparationAsync(c.OrderId, physicalCommand);
        var first = First(); await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var second = Record.ExceptionAsync(Second);
        try { await Web07OrderReadBarrier.WitnessAsync(c, barrier.OwnerSpid, secondSpid, "cancel-physical-" + dispatch + "-" + cancellationFirst); }
        finally { barrier.Release.TrySetResult(); }
        await first; Assert.IsType<ConflictAppException>(await second);
        await using var db = c.Context(); var parent = await db.StorefrontOrders.SingleAsync(x => x.Id == c.OrderId);
        if (cancellationFirst)
        {
            Assert.Equal(dispatch ? StorefrontOrderPhase.Preparing : StorefrontOrderPhase.Cancelled, parent.Phase);
            Assert.Equal(dispatch ? 4m : 0m, (await db.InventoryBalances.SingleAsync()).ReservedQty);
            Assert.Empty(await db.DeliveryJournalEntries.ToListAsync()); Assert.Empty(await db.DeliveryDispatchCostFragments.ToListAsync());
            if (dispatch) Assert.NotNull((await Web07Support.Cancellation(c, db).GetAsync(c.OrderId)).PendingRequestId);
        }
        else
        {
            Assert.Equal(dispatch ? StorefrontOrderPhase.Shipping : StorefrontOrderPhase.Preparing, parent.Phase);
            Assert.Empty(await db.Set<StorefrontCancellationRequest>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
            Assert.Equal(dispatch ? 6m : 10m, (await db.InventoryBalances.SingleAsync()).OnHandQty);
            Assert.Equal(dispatch ? 40_000m : 0m, await db.DeliveryDispatchCostFragments.SumAsync(x => x.CostAmount));
            // The customer can reload the genuine current state and request review;
            // no stale request is silently upgraded to a different phase operation.
            await using var retry = c.Context(); await Web07Support.Cancellation(c, retry, true).CancelGuestAsync(await c.CancelRequestAsync());
        }
        Assert.Empty(await db.Orders.ToListAsync()); Assert.Empty(await db.OrderPayments.ToListAsync());
        Assert.Empty(await db.Set<StorefrontRefundEvidence>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Real_bank_lookup_and_guest_cancel_in_either_order_retain_one_actual_credit_and_one_obligation_without_reopening_order(bool paidFirst)
    {
        var session = await Web07BankSession.CreateAsync(fixture); var c = session.Case;
        var originalCommand = await c.CancelRequestAsync(); session.Transport.Pay(session.Reference, 45_000m);
        if (paidFirst)
        {
            await session.ProcessAsync();
            await using (var db = c.Context()) await Assert.ThrowsAsync<ConflictAppException>(() => Web07Support.Cancellation(c, db, true).CancelGuestAsync(originalCommand));
            await using (var db = c.Context()) await Web07Support.Cancellation(c, db, true).CancelGuestAsync(await c.CancelRequestAsync());
        }
        else
        {
            var responseFixed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session.Transport.AfterTrustedLookup = async () => { responseFixed.TrySetResult(); await release.Task; };
            var processing = session.ProcessAsync(); await responseFixed.Task.WaitAsync(TimeSpan.FromSeconds(20));
            try { await using var db = c.Context(); await Web07Support.Cancellation(c, db, true).CancelGuestAsync(originalCommand); }
            finally { release.TrySetResult(); }
            await processing; session.Transport.AfterTrustedLookup = null;
        }
        await session.ProcessAsync(); var after = await c.SnapshotAsync(); await session.ProcessAsync(); Assert.Equal(after, await c.SnapshotAsync());
        await using var check = c.Context(); Assert.Equal(StorefrontOrderPhase.Cancelled, (await check.StorefrontOrders.SingleAsync()).Phase);
        Assert.Equal(45_000m, await check.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId).SumAsync(x => x.Amount));
        Assert.Single(await check.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId).ToListAsync());
        Assert.Single(await check.Set<StorefrontRefundFunding>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
        Assert.Equal(45_000m, await check.Set<StorefrontRefund>().Where(x => x.StorefrontOrderId == c.OrderId).SumAsync(x => x.Amount));
        Assert.Empty(await check.InventoryReservations.Where(x => x.StorefrontOrderId == c.OrderId && x.Status == InventoryReservationStatus.Active).ToListAsync());
        Assert.Empty(await check.Orders.ToListAsync()); Assert.Empty(await check.OrderPayments.ToListAsync());
    }

    [Fact]
    public async Task Two_real_execution_commands_for_the_same_actual_debit_serialize_and_commit_one_evidence_with_restart_replay()
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture, prepaidQr: true);
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, true).CancelGuestAsync(await c.CancelRequestAsync());
        int refund; await using (var db = c.Context()) refund = (await db.Set<StorefrontRefund>().SingleAsync()).Id;
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ApproveAsync(c.OrderId, await c.RefundDecisionAsync(refund, "WEB07 chứng cứ nguồn hợp lệ"));
        var debit = await Web07RecordedDebit.BankAsync(c, 230_000m);
        var firstRequest = await c.ExecutionRequestAsync(refund, debit.Source, c.Seed.LegalEntityId, c.Seed.BankAccountId, debit.ExternalReference, debit.ActualExecutionAt);
        var secondRequest = await c.ExecutionRequestAsync(refund, debit.Source, c.Seed.LegalEntityId, c.Seed.BankAccountId, debit.ExternalReference, debit.ActualExecutionAt);
        var flow = await Web07RefundExecutionTests.FlowAsync(c);
        using var barrier = new Web07OrderReadBarrier(); await using var firstDb = c.Context(barrier); await using var secondDb = c.Context();
        await secondDb.Database.OpenConnectionAsync(); var sid = await secondDb.Database.SqlQueryRaw<int>("SELECT CONVERT(int,@@SPID) AS [Value]").SingleAsync();
        var first = Web07Support.Refunds(c, firstDb).ConfirmExecutionAsync(c.OrderId, firstRequest); await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var second = Record.ExceptionAsync(() => Web07Support.Refunds(c, secondDb).ConfirmExecutionAsync(c.OrderId, secondRequest));
        try { await Web07OrderReadBarrier.WitnessAsync(c, barrier.OwnerSpid, sid, "same-payout-source"); }
        finally { barrier.Release.TrySetResult(); }
        await first; Assert.IsType<ConflictAppException>(await second);
        Assert.Equal(flow, await Web07RefundExecutionTests.FlowAsync(c));
        var after = await c.SnapshotAsync(); await using (var restart = c.Context()) await Web07Support.Refunds(c, restart).ConfirmExecutionAsync(c.OrderId, firstRequest);
        Assert.Equal(after, await c.SnapshotAsync()); await using var check = c.Context();
        Assert.Equal(230_000m, Assert.Single(await check.Set<StorefrontRefundEvidence>().Where(x => x.RefundId == refund).ToListAsync()).Amount);
    }
}

// Pauses a genuine full parent read after the production transaction/proof locks
// have been acquired. It changes no SQL, row, return value, or lock ordering.
internal sealed class Web07OrderReadBarrier : DbCommandInterceptor, IDisposable
{
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int OwnerSpid;
    private int once;
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
        InterceptionResult<DbDataReader> result, CancellationToken ct = default)
    {
        if (data.Context?.Database.CurrentTransaction is not null && command.CommandText.Contains("FROM [StorefrontOrders]", StringComparison.Ordinal) &&
            command.CommandText.Contains("[RecipientName]", StringComparison.Ordinal) && Interlocked.CompareExchange(ref once, 1, 0) == 0)
        {
            await using var sid = command.Connection!.CreateCommand(); sid.Transaction = command.Transaction; sid.CommandText = "SELECT CONVERT(int,@@SPID)";
            OwnerSpid = Assert.IsType<int>(await sid.ExecuteScalarAsync(ct)); Assert.True(OwnerSpid > 0); Entered.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
        }
        return result;
    }
    internal static async Task WitnessAsync(Web07Case c, int owner, int waiter, string label)
    {
        Assert.True(owner > 0 && waiter > 0 && owner != waiter); var polls = new List<object>(); var found = false;
        await using var observer = c.Context(); var database = await observer.Database.SqlQueryRaw<int>("SELECT CONVERT(int,DB_ID()) AS [Value]").SingleAsync();
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(7))
        {
            var rows = await observer.Database.SqlQueryRaw<WaitRow>("SELECT CONVERT(int,session_id) AS SessionId,CONVERT(int,blocking_session_id) AS BlockingSessionId,CONVERT(int,database_id) AS ResourceDatabaseId,wait_type AS WaitType,wait_resource AS WaitResource FROM sys.dm_exec_requests WHERE session_id={0} AND database_id={1}", waiter, database).ToListAsync();
            polls.Add(new { AtUtc = DateTime.UtcNow, Rows = rows });
            if (rows.Any(x => x.SessionId == waiter && x.BlockingSessionId == owner && x.ResourceDatabaseId == database)) { found = true; break; }
            await Task.Delay(40);
        }
        await File.WriteAllTextAsync(Path.Combine(c.Directory, label + "-sql-race-proof.json"), JsonSerializer.Serialize(new { OwnerSpid = owner, WaiterSpid = waiter, ResourceDatabaseId = database,
            Database = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(c.Seed.Database.ConnectionString).InitialCatalog, ObservedActualBlocking = found, Polls = polls }));
        Assert.True(found, "The actual second owned SQL session must wait on the actual first production transaction, not inferred timing.");
    }
    public void Dispose() => Release.TrySetResult();
    private sealed class WaitRow
    {
        public int SessionId { get; set; } public int BlockingSessionId { get; set; } public int ResourceDatabaseId { get; set; }
        public string? WaitType { get; set; } public string? WaitResource { get; set; }
    }
}
