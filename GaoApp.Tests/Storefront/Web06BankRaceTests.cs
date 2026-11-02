using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb06"), Trait("Category", "StorefrontWeb06")]
public sealed class Web06BankRaceTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_manual_account_gate_commit_before_waiting_native_import_preserves_full_secondary_as_ambiguous(bool crossStore)
    {
        var c = await Web06Case.CreateAsync(fixture, quantity: .9m); var native = await Web06CodSourceTests.NativeAsync(c);
        var bankInspection = await Web06BankInspection.CaptureAsync(c);
        var other = crossStore ? await Web06Case.CreateAsync(fixture, quantity: .9m) : c;
        var manual = await Web06ManualAmbiguityTests.ManualAsync(other, await Web06ManualAmbiguityTests.PhysicalAccount(c));
        await using var host = await Web06NotificationHost.StartAsync(c); var id = await Web06CodSourceTests.Accepted(host, native, Web06CodSourceTests.Upload(45_000m));
        await using (var db = c.Context()) Assert.False((await db.Set<StorefrontNotificationAttestation>().SingleAsync()).HadManualAmbiguity);
        var pause = new PauseSave<OrderPayment>(); await using var manualDb = other.Context(pause); await using var nativeDb = c.Context();
        await manualDb.Database.OpenConnectionAsync(); await nativeDb.Database.OpenConnectionAsync();
        var owner = ((SqlConnection)manualDb.Database.GetDbConnection()).ServerProcessId; var waiter = ((SqlConnection)nativeDb.Database.GetDbConnection()).ServerProcessId;
        var manualTask = Web06ManualAmbiguityTests.Payments(other, manualDb, manual.Terminal).ConfirmManualQrAsync(manual.Qr, default);
        await pause.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(10)); var importTask = Web06CodSourceTests.Importer(c, nativeDb).ImportAuthenticatedNotificationAsync(id);
        try { await Blocked(c, waiter, owner, "manual-first", crossStore); } finally { pause.Release.TrySetResult(); }
        await manualTask.WaitAsync(TimeSpan.FromSeconds(20)); var global = await importTask.WaitAsync(TimeSpan.FromSeconds(20));
        await using var check = c.Context(); var receipt = await check.Set<BankVerifiedReceipt>().SingleAsync(x => x.Id == global);
        Assert.Equal(45_000m, receipt.Amount); Assert.Equal(BankReceiptAssetConfidence.TransactionIdentityAmbiguous, receipt.AssetConfidence); Assert.Equal(BankReceiptRoutingProvenance.Unproven, receipt.RoutingProvenance);
        Assert.True(receipt.NeedsReview); Assert.Empty(await Web06CodSourceTests.Importer(c, check).ListAsync()); Assert.Empty(await bankInspection.Consumptions(check).ToListAsync()); await bankInspection.AssertDeltaAsync(check, receipts: 1, amount: 45_000m);
        await using var original = other.Context(); Assert.Equal(45_000m, (await original.OrderPayments.SingleAsync()).Amount);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_native_account_gate_commit_before_waiting_manual_confirmation_prevents_second_payment(bool crossStore)
    {
        var c = await Web06Case.CreateAsync(fixture, quantity: .9m); var native = await Web06CodSourceTests.NativeAsync(c);
        var bankInspection = await Web06BankInspection.CaptureAsync(c);
        var other = crossStore ? await Web06Case.CreateAsync(fixture, quantity: .9m) : c;
        var manual = await Web06ManualAmbiguityTests.ManualAsync(other, await Web06ManualAmbiguityTests.PhysicalAccount(c));
        await using var host = await Web06NotificationHost.StartAsync(c); var id = await Web06CodSourceTests.Accepted(host, native, Web06CodSourceTests.Upload(45_000m));
        var pause = new PauseSave<BankVerifiedReceipt>(); await using var nativeDb = c.Context(pause); await using var manualDb = other.Context();
        await nativeDb.Database.OpenConnectionAsync(); await manualDb.Database.OpenConnectionAsync();
        var owner = ((SqlConnection)nativeDb.Database.GetDbConnection()).ServerProcessId; var waiter = ((SqlConnection)manualDb.Database.GetDbConnection()).ServerProcessId;
        var importTask = Web06CodSourceTests.Importer(c, nativeDb).ImportAuthenticatedNotificationAsync(id); await pause.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var manualTask = Web06ManualAmbiguityTests.Payments(other, manualDb, manual.Terminal).ConfirmManualQrAsync(manual.Qr, default);
        try { await Blocked(c, waiter, owner, "native-first", crossStore); } finally { pause.Release.TrySetResult(); }
        var global = await importTask.WaitAsync(TimeSpan.FromSeconds(20)); var error = await Assert.ThrowsAsync<InvalidOperationException>(() => manualTask.WaitAsync(TimeSpan.FromSeconds(20)));
        Assert.Contains("nguồn thông báo ngân hàng độc lập", error.Message);
        await using var check = c.Context(); Assert.Equal(45_000m, (await check.Set<BankVerifiedReceipt>().SingleAsync(x => x.Id == global)).Amount);
        Assert.Equal(BankReceiptAssetConfidence.CertifiedDistinct, (await check.Set<BankVerifiedReceipt>().SingleAsync(x => x.Id == global)).AssetConfidence);
        await using var otherCheck = other.Context(); Assert.Empty(await otherCheck.OrderPayments.ToListAsync()); Assert.Empty(await bankInspection.Consumptions(check).ToListAsync()); await bankInspection.AssertDeltaAsync(check, receipts: 1, amount: 45_000m);
    }
    private static async Task Blocked(Web06Case c, int waiter, int blocker, string scenario, bool crossStore)
    {
        await using var observer = c.Context(); var seen = false;
        for (var attempt = 0; attempt < 100 && !seen; attempt++)
        {
            // Read only these two owned GUID-fixture sessions; no unrelated SQL data or server mutation.
            seen = await observer.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.dm_exec_requests WHERE session_id={0} AND blocking_session_id={1}", waiter, blocker).SingleAsync() == 1;
            if (!seen) await Task.Delay(50);
        }
        await File.WriteAllTextAsync(Web06Support.Artifact("bank-race/" + Guid.NewGuid().ToString("N") + ".json"), System.Text.Json.JsonSerializer.Serialize(new { Scenario = scenario, CrossStore = crossStore, WaiterSession = waiter, BlockingSession = blocker, ActualSqlBlockingObserved = seen, AtUtc = DateTime.UtcNow }));
        Assert.True(seen, "The actual competing SQL session must wait for the account-evidence owner before release.");
    }
    private sealed class PauseSave<T> : SaveChangesInterceptor where T : class
    {
        internal readonly TaskCompletionSource Arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int entered;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Interlocked.CompareExchange(ref entered, 1, 0) == 0 && data.Context!.ChangeTracker.Entries<T>().Any(x => x.State == EntityState.Added))
            { Arrived.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), ct); }
            else if (!Arrived.Task.IsCompleted) Volatile.Write(ref entered, 0);
            return result;
        }
    }
}
