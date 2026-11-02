using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Services.Storefront;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.SqlClient;
using System.Data.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb09"), Trait("Category", "StorefrontWeb09")]
public sealed class Web09PauseTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData("stop")]
    [InlineData("place")]
    public async Task Actual_SQL_configuration_lock_owner_and_blocked_waiter_prove_both_ordered_stop_place_outcomes(string first)
    {
        var c = await Web09Case.CreateAsync(fixture); await c.ActivateAsync(); var place = Web04Case.PlaceRequest(await c.QuoteAsync()); var stop = await c.CommandAsync();
        var probe = new ConfigOwnerProbe(c.StoreId);
        await using var owner = c.Seed.Database.CreateTenantContext(c.StoreId, probe); await owner.Database.OpenConnectionAsync();
        await using var waiter = c.Context(); await waiter.Database.OpenConnectionAsync();
        await using var spidCommand = waiter.Database.GetDbConnection().CreateCommand(); spidCommand.CommandText = "SELECT @@SPID";
        var waiterId = Convert.ToInt32(await spidCommand.ExecuteScalarAsync());
        async Task<string> Place(GaoApp.Infrastructure.Data.AppDbContext db)
        {
            try { var result = await c.Commerce(db).PlaceAsync(place); Assert.Equal(StorefrontPlacementStatus.Placed, result.Status); return "placed"; }
            catch (ValidationAppException) { return "closed"; }
        }
        async Task<string> Stop(GaoApp.Infrastructure.Data.AppDbContext db) { await c.Sales(db).StopNewOrdersAsync(stop); return "stopped"; }
        var ownerRun = first == "stop" ? Stop(owner) : Place(owner); Task<string>? waiterRun = null; object? waitingProof = null; string? ownerOutcome = null, waiterOutcome = null;
        try
        {
            var held = await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); Assert.Equal("Exclusive", held.Mode);
            waiterRun = first == "stop" ? Place(waiter) : Stop(waiter);
            waitingProof = await WaitForActualBlockedApplicationLock(c, held.SessionId, waiterId);
            probe.Release.TrySetResult(); ownerOutcome = await ownerRun; waiterOutcome = await waiterRun;
            Assert.Equal(first == "stop" ? "stopped" : "placed", ownerOutcome); Assert.Equal(first == "stop" ? "closed" : "stopped", waiterOutcome);
            await using var verify = c.Context(); Assert.False((await verify.StorefrontSettings.SingleAsync()).IsEnabled);
            Assert.Equal(first == "place" ? 1 : 0, await verify.StorefrontOrders.CountAsync());
            Assert.Equal(first == "place" ? 1 : 0, await verify.Set<StorefrontCommandReceipt>().CountAsync(x => x.ClientRequestId == place.ClientRequestId));
            Assert.Empty(await verify.OrderPayments.ToListAsync()); Assert.Empty(await verify.InventoryTransactions.ToListAsync()); Assert.Empty(await verify.Orders.ToListAsync());
        }
        finally
        {
            probe.Release.TrySetResult();
            try { await ownerRun; } catch { /* Original failure is preserved; only settle the known task before disposing its context. */ }
            if (waiterRun is not null) try { await waiterRun; } catch { }
            await File.WriteAllTextAsync(Web09Support.Artifact("lock-order/" + Guid.NewGuid().ToString("N") + ".json"), JsonSerializer.Serialize(new {
                AtUtc = DateTime.UtcNow, Owner = Web09Support.Owner(), FirstOperation = first, Held = probe.Witness, Waiting = waitingProof,
                OwnerOutcome = ownerOutcome, WaiterOutcome = waiterOutcome, ActualSqlObserved = waitingProof is not null,
                NoTimingInferredOrdering = true, NoProductionOrFinancialStateForced = true }));
        }
    }

    [Fact]
    public async Task Stop_blocks_new_quotes_and_uncommitted_placement_but_preserves_exact_existing_receipt_and_guest_access()
    {
        var c = await Web09Case.CreateAsync(fixture); await c.ActivateAsync();
        var committedRequest = Web04Case.PlaceRequest(await c.QuoteAsync()); var committed = await c.PlaceAsync(committedRequest);
        Assert.Equal(StorefrontPlacementStatus.Placed, committed.Status); Assert.NotNull(committed.Order); Assert.Equal(45000m, committed.Order.TotalDue);
        var pendingRequest = Web04Case.PlaceRequest(await c.QuoteAsync());
        await c.StopAsync(); var before = await c.Checkout.CountsAsync();
        await Assert.ThrowsAsync<ValidationAppException>(() => c.QuoteAsync());
        await Assert.ThrowsAsync<ValidationAppException>(() => c.PlaceAsync(pendingRequest));
        // Fresh service/provider loads the new retained DPAPI ring in place.
        var replay = await c.PlaceAsync(committedRequest); Assert.Equal(StorefrontPlacementStatus.Placed, replay.Status); Assert.Equal(committed.Order, replay.Order);
        Assert.Null(replay.ReplacementQuote); Assert.Equal("Đã nhận lại đúng đơn được tạo trước đó.", replay.Message);
        Assert.Equal("Cửa hàng đã nhận đơn và đang chờ chuẩn bị. Thanh toán COD khi nhận hàng.", committed.Message);
        Assert.Equal(before, await c.Checkout.CountsAsync());
        await using var db = c.Context(); var guest = await c.Commerce(db).GetGuestOrderAsync(Web04Support.GuestToken(committed.Order));
        Assert.NotNull(guest); Assert.Equal(committed.Order.Code, guest.Code); Assert.Equal(45000m, guest.TotalDue);
        var changed = new StorefrontPlaceOrderRequest { ClientRequestId = committedRequest.ClientRequestId,
            QuoteToken = committedRequest.QuoteToken, Fingerprint = new string('0', 64) };
        await Assert.ThrowsAsync<ConflictAppException>(() => c.PlaceAsync(changed));
        Assert.Equal(before, await c.Checkout.CountsAsync());
    }

    [Fact]
    public async Task Existing_guest_can_genuinely_cancel_after_stop_without_reopening_sales_or_creating_money()
    {
        var c = await Web09Case.CreateAsync(fixture); await c.ActivateAsync(); var placed = await c.PlaceAsync(Web04Case.PlaceRequest(await c.QuoteAsync()));
        Assert.NotNull(placed.Order); var token = Web04Support.GuestToken(placed.Order); await c.StopAsync();
        await using var db = c.Context(); var order = await db.StorefrontOrders.AsNoTracking().SingleAsync();
        var request = new StorefrontGuestCancellationRequest { Token = token, Reason = "WEB09 actual customer cancellation while sales paused",
            ClientRequestId = Guid.NewGuid(), ExpectedRevision = order.Revision, ExpectedVersion = Convert.ToBase64String(order.RowVersion) };
        var service = new StorefrontCancellationService(db, new Web01Actor(null, false), new InventoryBalanceRepository(db), c.Checkout.Clock);
        await service.CancelGuestAsync(request);
        await using var verify = c.Context(); var actual = await verify.StorefrontOrders.AsNoTracking().SingleAsync();
        Assert.Equal(StorefrontOrderPhase.Cancelled, actual.Phase); Assert.False((await verify.StorefrontSettings.SingleAsync()).IsEnabled);
        Assert.Empty(await verify.OrderPayments.ToListAsync()); Assert.Empty(await verify.InventoryTransactions.ToListAsync()); Assert.Empty(await verify.Orders.ToListAsync());
        Assert.Equal(1, await verify.Set<StorefrontCommandReceipt>().CountAsync(x => x.ClientRequestId == request.ClientRequestId));
        var beforeRetry = await c.Checkout.CountsAsync();
        await using var replayDb = c.Context();
        await new StorefrontCancellationService(replayDb, new Web01Actor(null, false), new InventoryBalanceRepository(replayDb), c.Checkout.Clock).CancelGuestAsync(request);
        Assert.Equal(beforeRetry, await c.Checkout.CountsAsync());
    }

    [Fact]
    public async Task Paused_committed_receipt_and_guest_capability_cannot_cross_another_genuinely_activated_tenant()
    {
        var c = await Web09Case.CreateAsync(fixture); var foreign = await Web09Case.CreateAsync(fixture);
        await c.ActivateAsync(); await foreign.ActivateAsync();
        var command = Web04Case.PlaceRequest(await c.QuoteAsync()); var placed = await c.PlaceAsync(command); Assert.NotNull(placed.Order);
        await c.StopAsync(); var original = await c.Checkout.CountsAsync(); var other = await foreign.Checkout.CountsAsync();
        await Assert.ThrowsAsync<NotFoundAppException>(() => foreign.PlaceAsync(command));
        await using var otherDb = foreign.Context(); Assert.Null(await foreign.Commerce(otherDb).GetGuestOrderAsync(Web04Support.GuestToken(placed.Order)));
        Assert.Equal(original, await c.Checkout.CountsAsync()); Assert.Equal(other, await foreign.Checkout.CountsAsync());
    }

    [Fact]
    public async Task Concurrent_stop_and_placement_finish_closed_with_zero_or_one_real_order_and_preserve_an_existing_sibling_hold()
    {
        var c = await Web09Case.CreateAsync(fixture); await c.ActivateAsync();
        var sibling = await c.PlaceAsync(Web04Case.PlaceRequest(await c.QuoteAsync())); Assert.NotNull(sibling.Order);
        await using var beforeDb = c.Context(); var siblingId = await beforeDb.StorefrontOrders.Where(x => x.Code == sibling.Order.Code).Select(x => x.Id).SingleAsync();
        var siblingHolds = await (from link in beforeDb.Set<StorefrontReservationLink>() join hold in beforeDb.InventoryReservations on link.InventoryReservationId equals hold.Id
                                  where link.StorefrontOrderId == siblingId select new { hold.Id, hold.ReservedQty, hold.Status }).ToListAsync();
        Assert.NotEmpty(siblingHolds); var command = Web04Case.PlaceRequest(await c.QuoteAsync()); var stop = await c.CommandAsync();
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<StorefrontPlacementResult?> Place()
        {
            await go.Task; try { return await c.PlaceAsync(command); }
            catch (ValidationAppException) { return null; } catch (ConflictAppException) { return null; }
        }
        async Task Stop()
        { await go.Task; await using var db = c.Context(); await c.Sales(db).StopNewOrdersAsync(stop); }
        var placement = Place(); var stopping = Stop(); go.SetResult(); await stopping; var result = await placement;
        await using var after = c.Context(); Assert.False((await after.StorefrontSettings.AsNoTracking().SingleAsync()).IsEnabled);
        var orderCount = await after.StorefrontOrders.CountAsync(); Assert.Equal(result is null ? 1 : 2, orderCount);
        Assert.Equal(result is null ? 0 : 1, await after.Set<StorefrontCommandReceipt>().CountAsync(x => x.ClientRequestId == command.ClientRequestId));
        var unchanged = await (from link in after.Set<StorefrontReservationLink>() join hold in after.InventoryReservations on link.InventoryReservationId equals hold.Id
                               where link.StorefrontOrderId == siblingId select new { hold.Id, hold.ReservedQty, hold.Status }).ToListAsync();
        Assert.Equal(siblingHolds, unchanged); Assert.Empty(await after.InventoryTransactions.ToListAsync()); Assert.Empty(await after.OrderPayments.ToListAsync());
        await Assert.ThrowsAsync<ValidationAppException>(() => c.QuoteAsync());
    }

    private sealed record HeldLock(int SessionId, string Mode, string Resource, string PendingSqlSHA256, DateTime AtUtc);
    private sealed class ConfigOwnerProbe(int store) : DbCommandInterceptor
    {
        private int captured;
        internal TaskCompletionSource<HeldLock> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal HeldLock? Witness { get; private set; }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (command.Transaction is null || !command.CommandText.Contains("[StorefrontSettings]", StringComparison.Ordinal) || Interlocked.CompareExchange(ref captured, 1, 0) != 0) return result;
            try
            {
                await using var inspect = command.Connection!.CreateCommand(); inspect.Transaction = command.Transaction;
                inspect.CommandText = "SELECT @@SPID,APPLOCK_MODE(N'public',@resource,N'Transaction')";
                var parameter = inspect.CreateParameter(); parameter.ParameterName = "@resource"; parameter.Value = "GaoApp:Storefront:Configuration:" + store; inspect.Parameters.Add(parameter);
                await using (var reader = await inspect.ExecuteReaderAsync(ct))
                {
                    Assert.True(await reader.ReadAsync(ct)); Witness = new(Convert.ToInt32(reader[0]), reader.GetString(1), (string)parameter.Value,
                        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(command.CommandText))), DateTime.UtcNow);
                    Assert.Equal("Exclusive", Witness.Mode);
                }
                Entered.TrySetResult(Witness!); await Release.Task.WaitAsync(ct); return result;
            }
            catch (Exception error) { Entered.TrySetException(error); throw; }
        }
    }
    private static async Task<object> WaitForActualBlockedApplicationLock(Web09Case c, int holder, int waiter)
    {
        await using var connection = new SqlConnection(c.Seed.Database.ConnectionString); await connection.OpenAsync(); var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(10))
        {
            await using var command = new SqlCommand("SELECT r.session_id,r.blocking_session_id,r.wait_type,h.resource_description,h.request_mode,w.request_mode,h.request_status,w.request_status FROM sys.dm_exec_requests r JOIN sys.dm_tran_locks w ON w.request_session_id=r.session_id AND w.resource_type='APPLICATION' JOIN sys.dm_tran_locks h ON h.request_session_id=@holder AND h.resource_type='APPLICATION' AND h.resource_database_id=w.resource_database_id AND h.resource_description=w.resource_description WHERE r.session_id=@waiter AND r.blocking_session_id=@holder AND h.request_status='GRANT' AND w.request_status='WAIT'", connection);
            command.Parameters.AddWithValue("@holder", holder); command.Parameters.AddWithValue("@waiter", waiter);
            await using (var reader = await command.ExecuteReaderAsync())
                if (await reader.ReadAsync())
                {
                    var wait = reader.GetString(2); Assert.StartsWith("LCK_M_", wait, StringComparison.Ordinal);
                    Assert.Equal("X", reader.GetString(4)); Assert.Equal("X", reader.GetString(5));
                    return new { AtUtc = DateTime.UtcNow, WaiterSessionId = Convert.ToInt32(reader[0]), BlockingSessionId = Convert.ToInt32(reader[1]),
                        WaitType = wait, ExactSharedResourceDescription = reader.GetString(3), GrantedMode = reader.GetString(4), WaitingMode = reader.GetString(5),
                        GrantedStatus = reader.GetString(6), WaitingStatus = reader.GetString(7), OwnedTwoSessionsOnly = true };
                }
            await Task.Delay(50);
        }
        throw new TimeoutException("The second owned session was not actually observed waiting on the first session's same APPLICATION lock.");
    }
}
