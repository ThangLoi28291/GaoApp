using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Domain.Delivery;
using GaoApp.Infrastructure.Data;
using GaoApp.Tests.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace GaoApp.Tests.Delivery;

[Collection("DeliveryD07"), Trait("Category", "DeliveryD07")]
public sealed class DeliveryD07SqlServerTests(DeliveryD02Fixture fixture, ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("Full", "4", "0")]
    [InlineData("Partial", "2.5", "1.5")]
    [InlineData("NotDelivered", "0", "4")]
    [InlineData("Refused", "0", "4")]
    [InlineData("Unreachable", "0", "4")]
    [InlineData("Cancelled", "0", "4")]
    public async Task T01_Actual_departed_outcomes_preserve_sources_and_only_append_report_facts(string kind, string delivered, string returned)
    {
        using var c = await DeliveryD07Case.CreateAsync(fixture);
        var stock = await c.StockAndFinanceAsync(); var source = await c.SourceAsync(); var other = await c.OtherOrderAsync();
        var request = (await c.RequestAsync(outcome: kind, delivered: delivered, returned: returned)) with {
            ReportedCashText = "120000", ReportedTransferText = "35000",
            CustomerReceivedAtUtc = (await c.Trip.DetailAsync()).DepartedAtUtc };
        var ack = await c.ReportAsync(request);
        Assert.False(ack.Replayed); Assert.Equal("outcome", ack.Operation); Assert.Equal(2, ack.Orders.Count);
        var detail = await c.Trip.DetailAsync(); var row = detail.Orders.Single(x => x.DeliveryOrderId == c.Id());
        var reported = Assert.IsType<DeliveryOrderOutcomeDto>(row.Outcome); var line = Assert.Single(reported.Lines);
        Assert.Equal(kind, reported.Outcome); Assert.Equal(delivered, line.DeliveredBaseQuantityText);
        Assert.Equal(returned, line.ReportedReturnBaseQuantityText); Assert.Equal(returned, line.PendingReturnBaseQuantityText);
        Assert.Equal("0", line.AcceptedReturnedAtReportBaseQuantityText);
        Assert.Equal("120000", reported.ReportedCashText); Assert.Equal("35000", reported.ReportedTransferText);
        Assert.Equal(c.Actor.UserId, reported.ActorUserId); Assert.Equal(c.Courier.UserId, reported.CourierUserId);
        Assert.Equal("Phone", reported.ReportSource); Assert.Equal(request.CustomerReceivedAtUtc, reported.CustomerReceivedAtUtc);
        Assert.InRange(reported.RecordedAtUtc, detail.DepartedAtUtc!.Value, detail.ServerTimeUtc);
        Assert.Equal(nameof(DeliveryState.AwaitingReconciliation), row.State);
        Assert.Equal(nameof(DeliveryTripState.Departed), detail.State); Assert.False(detail.ReadyForReconciliation);
        Assert.Equal(stock, await c.StockAndFinanceAsync()); Assert.Equal(source, await c.SourceAsync());
        Assert.Equal(other, await c.OtherOrderAsync());
        await using var db = c.Context();
        var orderId = c.Id();
        var revision = Assert.Single(await db.DeliveryRevisions.Where(x => x.DeliveryOrderId == orderId && x.Action == "trip-outcome").ToArrayAsync());
        var saved = JsonSerializer.Deserialize<DeliveryOutcomeSnapshot>(revision.SnapshotJson, Json)!;
        Assert.Equal("delivery-outcome-v1", saved.Schema); Assert.Equal(request.ClientRequestId, saved.RootClientRequestId);
        Assert.Equal(c.Id(), saved.DeliveryOrderId); Assert.Equal(c.TripId, saved.TripId);
        Assert.Equal(c.Actor.Store.WarehouseId, saved.SourceWarehouseId);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(revision.SnapshotJson))), revision.SnapshotHash, ignoreCase: true);
        Assert.Single(await db.DeliveryTripCommandReceipts.Where(x => x.ClientRequestId == request.ClientRequestId).ToArrayAsync());
    }

    [Fact]
    public async Task T02_Declared_returns_include_already_accepted_goods_and_never_repost_the_return()
    {
        using var c = await DeliveryD07Case.CreateAsync(fixture);
        await c.Order().Inventory.ReceiveAsync("0.5");
        var before = await c.SnapshotAsync(); var stock = await c.StockAndFinanceAsync();
        var invalid = await c.RequestAsync(outcome: "Partial", delivered: "3.75", returned: "0.25");
        await Reject(() => c.ReportAsync(invalid), "OUTCOME_QUANTITY_INVALID"); Assert.Equal(before, await c.SnapshotAsync());
        await c.ReportAsync(await c.RequestAsync(outcome: "Partial", delivered: "3", returned: "1"));
        var line = Assert.Single((await c.Trip.DetailAsync()).Orders.Single(x => x.DeliveryOrderId == c.Id()).Outcome!.Lines);
        Assert.Equal("0.5", line.AcceptedReturnedAtReportBaseQuantityText); Assert.Equal("0.5", line.PendingReturnBaseQuantityText);
        Assert.Equal(stock, await c.StockAndFinanceAsync());
        // A genuine later warehouse receipt retains the original declaration but makes it stale.
        await c.Order().Inventory.ReceiveAsync("1");
        var detail = await c.Trip.DetailAsync(); var saved = detail.Orders.Single(x => x.DeliveryOrderId == c.Id()).Outcome!;
        Assert.False(saved.IsCurrentValid); Assert.Equal("-0.5", Assert.Single(saved.Lines).PendingReturnBaseQuantityText);
        Assert.False(detail.AllOutcomesComplete); Assert.False(detail.ReadyForReconciliation);
        stock = await c.StockAndFinanceAsync();
        await c.ReportAsync(await c.RequestAsync(outcome: "Partial", delivered: "2.5", returned: "1.5"));
        saved = (await c.Trip.DetailAsync()).Orders.Single(x => x.DeliveryOrderId == c.Id()).Outcome!;
        Assert.True(saved.IsCurrentValid); Assert.Equal("0", Assert.Single(saved.Lines).PendingReturnBaseQuantityText);
        Assert.Equal(stock, await c.StockAndFinanceAsync());
    }

    [Fact]
    public async Task T03_Return_first_then_every_order_report_and_correction_never_posts_stock_money_or_locks_outcomes()
    {
        using var c = await DeliveryD07Case.CreateAsync(fixture); var source = await c.SourceAsync();
        var stock = await c.StockAndFinanceAsync(); var returnRequest = await c.ReturnRequestAsync();
        var arrival = await c.ArrivalAsync(returnRequest); Assert.Equal("return-to-store", arrival.Operation);
        var detail = await c.Trip.DetailAsync(); Assert.NotNull(detail.ReturnToStore);
        Assert.False(detail.AllOutcomesComplete); Assert.False(detail.ReadyForReconciliation);
        Assert.All(detail.Orders, x => Assert.Equal(nameof(DeliveryState.Delivering), x.State));
        Assert.Equal(stock, await c.StockAndFinanceAsync());
        var other = await c.OtherOrderAsync();
        await c.ReportAsync(await c.RequestAsync()); Assert.Equal(other, await c.OtherOrderAsync());
        await c.ReportAsync(await c.RequestAsync(index: 1, delivered: "3"), index: 1);
        detail = await c.Trip.DetailAsync(); Assert.True(detail.AllOutcomesComplete); Assert.True(detail.ReadyForReconciliation);
        Assert.All(detail.Orders, x => Assert.Equal(nameof(DeliveryState.AwaitingReconciliation), x.State));
        await c.ReportAsync(await c.RequestAsync(outcome: "Partial", delivered: "2.5", returned: "1.5"));
        detail = await c.Trip.DetailAsync(); Assert.True(detail.ReadyForReconciliation);
        Assert.Equal("Partial", detail.Orders.Single(x => x.DeliveryOrderId == c.Id()).Outcome!.Outcome);
        var after = await c.SnapshotAsync(); var replay = await c.ArrivalAsync(returnRequest);
        Assert.True(replay.Replayed); Assert.Equal(arrival.AppliedVersion, replay.AppliedVersion);
        Assert.Equal(after, await c.SnapshotAsync()); Assert.Equal(stock, await c.StockAndFinanceAsync());
        Assert.Equal(source, await c.SourceAsync());
    }

    [Fact]
    public async Task T04_Exact_replay_survives_old_versions_but_changed_body_and_new_stale_commands_cannot_overwrite()
    {
        using var c = await DeliveryD07Case.CreateAsync(fixture); var request = await c.RequestAsync();
        var ack = await c.ReportAsync(request); var after = await c.SnapshotAsync();
        var replay = await c.ReportAsync(request); Assert.True(replay.Replayed);
        Assert.Equal(ack.AppliedRevision, replay.AppliedRevision); Assert.Equal(ack.AppliedVersion, replay.AppliedVersion);
        Assert.Equal(after, await c.SnapshotAsync());
        await Reject(() => c.ReportAsync(request with { ReportedCashText = "1" }), "REQUEST_KEY_CONFLICT");
        await Reject(() => c.ReportAsync(request with { ClientRequestId = Guid.NewGuid() }), "VERSION_CONFLICT");
        Assert.Equal(after, await c.SnapshotAsync());
        await c.ReportAsync(await c.RequestAsync(outcome: "Partial", delivered: "3", returned: "1"));
        var corrected = await c.SnapshotAsync(); Assert.True((await c.ReportAsync(request)).Replayed);
        Assert.Equal(corrected, await c.SnapshotAsync());
    }

    [Fact]
    public async Task T05_Pending_recall_can_record_zero_delivery_and_positive_reported_money_without_completing_cancellation()
    {
        using var c = await DeliveryD07Case.CreateAsync(fixture); var detail = await c.Trip.DetailAsync();
        await using (var db = c.Context())
            await c.Trip.Service(db).RequestCancellationAsync(c.TripId, c.Id(), new(Guid.NewGuid(), detail.Version,
                detail.Orders.Single(x => x.DeliveryOrderId == c.Id()).Version, "Khách yêu cầu thu hồi"));
        var stock = await c.StockAndFinanceAsync(); var other = await c.OtherOrderAsync();
        await c.ReportAsync((await c.RequestAsync(outcome: "Cancelled", delivered: "0", returned: "4")) with { ReportedCashText = "15000" });
        detail = await c.Trip.DetailAsync(); var row = detail.Orders.Single(x => x.DeliveryOrderId == c.Id());
        Assert.Equal(nameof(DeliveryState.CancellationPending), row.State); Assert.Equal("15000", row.Outcome!.ReportedCashText);
        Assert.Equal(stock, await c.StockAndFinanceAsync()); Assert.Equal(other, await c.OtherOrderAsync());
    }

    [Fact]
    public async Task T06_Failure_after_outcome_revision_rolls_back_and_the_same_request_can_commit_once()
    {
        using var c = await DeliveryD07Case.CreateAsync(fixture); var request = await c.RequestAsync(); var before = await c.SnapshotAsync();
        await Assert.ThrowsAsync<D07InjectedFailure>(() => c.ReportAsync(request, interceptor: new D07ReceiptFailure()));
        Assert.Equal(before, await c.SnapshotAsync());
        Assert.False((await c.ReportAsync(request)).Replayed); var after = await c.SnapshotAsync();
        Assert.True((await c.ReportAsync(request)).Replayed); Assert.Equal(after, await c.SnapshotAsync());
    }

    [Fact]
    public async Task T07_Physical_arrival_requires_the_actual_courier_and_rolls_back_before_receipt_on_failure()
    {
        using var c = await DeliveryD07Case.CreateAsync(fixture); var request = await c.ReturnRequestAsync(); var before = await c.SnapshotAsync();
        await Reject(() => c.ArrivalAsync(request with { PhysicalReturnConfirmed = false }), "PHYSICAL_RETURN_REQUIRED");
        await Reject(() => c.ArrivalAsync(request with { ConfirmedCourierUserId = c.Trip.OtherCourier.UserId }), "COURIER_MISMATCH");
        Assert.Equal(before, await c.SnapshotAsync());
        await Assert.ThrowsAsync<D07InjectedFailure>(() => c.ArrivalAsync(request, interceptor: new D07ReceiptFailure()));
        Assert.Equal(before, await c.SnapshotAsync());
        Assert.False((await c.ArrivalAsync(request)).Replayed); var after = await c.SnapshotAsync();
        Assert.True((await c.ArrivalAsync(request)).Replayed); Assert.Equal(after, await c.SnapshotAsync());
        await Reject(() => c.ArrivalAsync(request with { ConfirmationNote = "Nội dung khác" }), "REQUEST_KEY_CONFLICT");
        Assert.Equal(after, await c.SnapshotAsync());
    }

    [Fact]
    public async Task T08_Invalid_source_line_and_declared_time_fail_without_any_partial_report()
    {
        using var c = await DeliveryD07Case.CreateAsync(fixture); var request = await c.RequestAsync(); var before = await c.SnapshotAsync();
        foreach (var invalid in new[] { request with { CustomerReceivedAtUtc = DateTimeOffset.UtcNow.AddDays(1) },
            request with { CustomerReceivedAtUtc = (await c.Trip.DetailAsync()).DepartedAtUtc!.Value.AddSeconds(-1) } })
        { await Reject(() => c.ReportAsync(invalid), "CUSTOMER_TIME_INVALID"); Assert.Equal(before, await c.SnapshotAsync()); }
        var foreignLine = Assert.Single((await c.Trip.DetailAsync()).Orders.Single(x => x.DeliveryOrderId == c.Id(1)).Lines);
        var badLine = request with { Lines = [new(foreignLine.LineId, "4", "0")] };
        await Reject(() => c.ReportAsync(badLine), "LINE_INVALID");
        Assert.Equal(before, await c.SnapshotAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task T09_Two_real_SQL_workers_share_exact_replay_or_have_one_conflicting_revision_winner(bool sameCommand)
    {
        using var c = await DeliveryD07Case.CreateAsync(fixture); var request = await c.RequestAsync();
        var stock = await c.StockAndFinanceAsync(); var untouched = await c.OtherOrderAsync();
        var other = sameCommand ? request : request with { ClientRequestId = Guid.NewGuid(), ReportedCashText = "1" };
        await using var first = c.Context(); await using var second = c.Context();
        await first.Database.OpenConnectionAsync(); await second.Database.OpenConnectionAsync();
        var workers = new[] { await Worker(first), await Worker(second) };
        async Task<(DeliveryTripCommandAck? Ack, Exception? Error)> Attempt(AppDbContext db, DeliveryOutcomeRequest payload)
        { try { return (await c.Trip.Service(db).RecordOutcomeAsync(c.TripId, c.Id(), payload), null); } catch (Exception error) { return (null, error); } }
        var results = await Gate<(DeliveryTripCommandAck? Ack, Exception? Error)>(c, workers, () => new[] { Attempt(first, request), Attempt(second, other) },
            sameCommand ? "D07_SQL_SAME_REPORT_BARRIER_PROOF" : "D07_SQL_CONFLICTING_REPORT_BARRIER_PROOF");
        if (sameCommand) { Assert.All(results, x => Assert.Null(x.Error)); Assert.Single(results, x => x.Ack!.Replayed); Assert.Single(results, x => !x.Ack!.Replayed); }
        else { Assert.Single(results, x => x.Ack is not null); Assert.Equal("VERSION_CONFLICT", Assert.IsType<DeliveryFoundationException>(Assert.Single(results, x => x.Error is not null).Error).Code); }
        await using var verify = c.Context();
        var orderId = c.Id();
        Assert.Single(await verify.DeliveryRevisions.Where(x => x.DeliveryOrderId == orderId && x.Action == "trip-outcome").ToArrayAsync());
        Assert.Single(await verify.DeliveryTripCommandReceipts.Where(x => x.TripId == c.TripId && x.Operation == "outcome").ToArrayAsync());
        Assert.Equal(stock, await c.StockAndFinanceAsync()); Assert.Equal(untouched, await c.OtherOrderAsync());
    }

    private static async Task Reject(Func<Task> action, string code)
    {
        var error = await Record.ExceptionAsync(action);
        Assert.NotNull(error);
        Assert.Equal(code, error switch { DeliveryRuleException rule => rule.Code,
            DeliveryFoundationException foundation => foundation.Code, _ => throw error });
    }
    private static async Task<int> Worker(AppDbContext db)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand(); command.CommandText = "SELECT @@SPID";
        var id = Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture); Assert.True(id > 0); return id;
    }
    private async Task<T[]> Gate<T>(DeliveryD07Case c, int[] workers, Func<Task<T>[]> start, string marker)
    {
        Assert.Equal(2, workers.Distinct().Count());
        await using var gate = LocalDbSqlConnectionFactory.Create(c.Trip.Web.Database.ConnectionString);
        await gate.OpenAsync(); Assert.Matches("^GaoApp_R2_InventoryPosting_[A-F0-9]{32}$", gate.Database);
        await using var tx = (SqlTransaction)await gate.BeginTransactionAsync();
        var resource = "GaoApp:Delivery:trip:" + c.Actor.Store.StoreId + ":" + c.TripId;
        await using (var command = gate.CreateCommand()) { command.Transaction = tx;
            command.CommandText = "DECLARE @r int;EXEC @r=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=1000;SELECT @r;";
            command.Parameters.AddWithValue("@resource", resource); Assert.True(Convert.ToInt32(await command.ExecuteScalarAsync()) >= 0); }
        string physicalResource;
        await using (var command = gate.CreateCommand())
        {
            command.Transaction = tx;
            command.CommandText = "SELECT resource_description FROM sys.dm_tran_locks WHERE resource_database_id=DB_ID() AND resource_type='APPLICATION' AND request_session_id=@@SPID AND request_mode='X' AND request_status='GRANT'";
            physicalResource = Assert.IsType<string>(await command.ExecuteScalarAsync());
        }
        var pending = start(); var paths = Array.Empty<int[]>(); var held = false; var waiting = false; var bothPending = false;
        try
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(8))
            {
                await using var command = gate.CreateCommand(); command.Transaction = tx;
                command.CommandText = "SELECT session_id,blocking_session_id,wait_type FROM sys.dm_exec_requests WHERE database_id=DB_ID() AND session_id<>@@SPID;SELECT request_session_id,request_mode,request_status,resource_description FROM sys.dm_tran_locks WHERE resource_database_id=DB_ID() AND resource_type='APPLICATION';";
                var requests = new List<(int Id, int Blocker, string? Wait)>(); var locks = new List<(int Id, string Mode, string Status, string Resource)>();
                await using (var reader = await command.ExecuteReaderAsync()) { while (await reader.ReadAsync()) requests.Add((reader.GetInt16(0), reader.GetInt16(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
                    await reader.NextResultAsync(); while (await reader.ReadAsync()) locks.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3))); }
                int[] Path(int id) { var path = new List<int>(); var seen = new HashSet<int>();
                    for (var depth = 0; depth < 32 && seen.Add(id); depth++) { path.Add(id); if (id == gate.ServerProcessId) return path.ToArray();
                        var row = requests.SingleOrDefault(x => x.Id == id); if (row.Blocker <= 0) break; id = row.Blocker; } return []; }
                paths = workers.Select(Path).Where(x => x.Length > 1).ToArray();
                held = locks.Count(x => x.Id == gate.ServerProcessId && x.Mode == "X" && x.Status == "GRANT" && x.Resource == physicalResource) == 1;
                waiting = workers.All(id => requests.Any(x => x.Id == id && x.Wait == "LCK_M_X") && locks.Any(x => x.Id == id && x.Mode == "X" && x.Status is "WAIT" or "CONVERT"));
                bothPending = pending.Length == 2 && pending.All(x => !x.IsCompleted);
                if (held && waiting && bothPending && paths.Length == 2) break;
                await Task.Delay(25);
            }
            output.WriteLine(marker + " " + JsonSerializer.Serialize(new { OwnedDatabase = gate.Database, GateSessionId = gate.ServerProcessId,
                RequestedResource = resource, ExactGateApplicationResource = physicalResource, DistinctWorkerSessionIds = workers, AcyclicGatePaths = paths,
                GateHeldBeforeRelease = held, BothActualSqlWorkersWaiting = waiting, BothTasksPendingBeforeRelease = bothPending }));
        }
        finally { await tx.RollbackAsync(); }
        var results = await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(35));
        Assert.Equal(2, paths.Length); Assert.True(held); Assert.True(waiting); Assert.True(bothPending); return results;
    }
}
