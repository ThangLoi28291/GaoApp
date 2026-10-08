using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Tests.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace GaoApp.Tests.Delivery;

[Collection("DeliveryD08"), Trait("Category", "DeliveryD08")]
public sealed class DeliveryD08SqlServerTests(DeliveryD02Fixture fixture, ITestOutputHelper output)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task S01_Two_real_HTTP_SQL_workers_commit_one_sale_and_return_or_have_one_conflicting_counter_winner(bool sameIntent)
    {
        using var c = await DeliveryD08Case.CreateAsync(fixture);
        var firstBody = await c.RequestAsync(3);
        using var secondSession = await fixture.Web.LoginAsync(c.Closer);
        var secondCounter = sameIntent ? default : await DeliveryD08Case.CounterAsync(fixture.Web, c.Order.Account.Store);
        try
        {
            var secondBody = sameIntent ? firstBody : firstBody with { ClientRequestId = Guid.NewGuid(),
                ExpectedTerminalId = secondCounter.Account.Store.TerminalId, ExpectedShiftId = secondCounter.ShiftId };
            var other = await c.Legacy.OtherOrderAsync(); var source = await c.Legacy.SourceAsync(); var proof = await c.DispatchProofAsync();
            async Task<(HttpStatusCode Status, DeliveryHandoverAck? Ack)> Send(HttpClient http, DeliveryHandoverRequest body)
            {
                using var response = await http.PostAsJsonAsync(c.Path, body);
                return (response.StatusCode, response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<DeliveryHandoverAck>() : null);
            }
            var results = await Gate<(HttpStatusCode Status, DeliveryHandoverAck? Ack)>(c, () => [Send(c.Client.Http, firstBody), Send(sameIntent ? secondSession.Http : secondCounter.Client.Http, secondBody)],
                sameIntent ? "D08_SQL_SAME_HANDOVER_BARRIER_PROOF" : "D08_SQL_CONFLICTING_HANDOVER_BARRIER_PROOF");
            if (sameIntent)
            {
                Assert.All(results, x => Assert.Equal(HttpStatusCode.OK, x.Status));
                Assert.Single(results, x => x.Ack!.Replayed); Assert.Single(results, x => !x.Ack!.Replayed);
                Assert.Equal(results[0].Ack!.Settlement.SaleOrderId, results[1].Ack!.Settlement.SaleOrderId);
            }
            else { Assert.Single(results, x => x.Status == HttpStatusCode.OK); Assert.Single(results, x => x.Status == HttpStatusCode.Conflict); }
            var winner = results.First(x => x.Ack is not null).Ack!; var sale = await c.SaleAsync(winner);
            Assert.Equal(60000, sale.GrandTotal); Assert.Single(sale.Payments); Assert.Equal(3, sale.Lines.Sum(x => x.BaseQuantity));
            await using var db = c.Context();
            Assert.Single(await db.DeliveryTripCommandReceipts.Where(x => x.TripId == c.Trip.TripId && x.Operation == "handover" && x.ClientRequestId == winner.ClientRequestId).ToArrayAsync());
            Assert.Single(await db.DeliveryRevisions.Where(x => x.DeliveryOrderId == c.Id && x.Action == "counter-handover").ToArrayAsync());
            Assert.Equal(1, await db.DeliveryReturnedCostFragments.Where(x => x.DeliveryOrderId == c.Id).SumAsync(x => x.BaseQuantity));
            Assert.Equal(60000, await db.POSShifts.Where(x => x.Id == c.ShiftId || (!sameIntent && x.Id == secondCounter.ShiftId)).SumAsync(x => x.CashSalesTotal));
            Assert.Equal(other, await c.Legacy.OtherOrderAsync()); Assert.Equal(source, await c.Legacy.SourceAsync()); Assert.Equal(proof, await c.DispatchProofAsync());
        }
        finally { if (!sameIntent) secondCounter.Client.Dispose(); }
    }

    [Fact]
    public async Task S02_Real_Web_failure_after_return_and_financial_writes_rolls_back_every_effect_then_retries_once()
    {
        using var c = await DeliveryD08Case.CreateAsync(fixture); var request = await c.RequestAsync(3);
        var before = await c.SnapshotAsync(); var name = "TR_D08_Fault_" + Guid.NewGuid().ToString("N")[..12];
        await using var db = c.Context(); await db.Database.OpenConnectionAsync();
        Assert.Matches("^GaoApp_R2_InventoryPosting_[A-F0-9]{32}$", db.Database.GetDbConnection().Database);
        Assert.True(c.Trip.TripId > 0);
        async Task ExecuteProbe(string ddl)
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = ddl; await command.ExecuteNonQueryAsync();
        }
        // Fixture-only fault after the actual goods/POS work, never a fake POS
        // adapter or an application fault switch. The production trigger stays intact.
        await ExecuteProbe($"CREATE TRIGGER [dbo].[{name}] ON [dbo].[DeliveryTripCommandReceipts] AFTER INSERT AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM inserted WHERE [TripId]={c.Trip.TripId} AND [Operation]=N'handover') THROW 59908,N'D08 owned financial rollback probe',1; END");
        try
        {
            using var response = await c.Client.Http.PostAsJsonAsync(c.Path, request);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal(before, await c.SnapshotAsync());
        }
        finally { await ExecuteProbe($"DROP TRIGGER [dbo].[{name}]"); }
        var ack = await c.ConfirmAsync(request); Assert.False(ack.Replayed);
        var after = await c.SnapshotAsync(); var replay = await c.ConfirmAsync(request); Assert.True(replay.Replayed);
        Assert.Equal(ack.Settlement, replay.Settlement); Assert.Equal(after, await c.SnapshotAsync());
        Assert.Equal(1, await db.DeliveryReturnedCostFragments.Where(x => x.DeliveryOrderId == c.Id).SumAsync(x => x.BaseQuantity));
        Assert.Single(await db.OrderPayments.Where(x => x.OrderId == ack.Settlement.SaleOrderId).ToArrayAsync());
        Assert.Single(await db.DeliveryTripCommandReceipts.Where(x => x.ClientRequestId == request.ClientRequestId).ToArrayAsync());
    }

    private async Task<T[]> Gate<T>(DeliveryD08Case c, Func<Task<T>[]> start, string marker)
    {
        await using var gate = LocalDbSqlConnectionFactory.Create(fixture.Web.Database.ConnectionString); await gate.OpenAsync();
        Assert.Matches("^GaoApp_R2_InventoryPosting_[A-F0-9]{32}$", gate.Database);
        await using var tx = (SqlTransaction)await gate.BeginTransactionAsync();
        int gateSessionId;
        await using (var command = gate.CreateCommand())
        {
            command.Transaction = tx; command.CommandText = "SELECT @@SPID";
            gateSessionId = Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
            Assert.True(gateSessionId > 0);
        }
        var resource = "GaoApp:Delivery:trip:" + c.Closer.Store.StoreId + ":" + c.Trip.TripId;
        await using (var command = gate.CreateCommand())
        {
            command.Transaction = tx;
            command.CommandText = "DECLARE @r int;EXEC @r=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=1000;SELECT @r;";
            command.Parameters.AddWithValue("@resource", resource); Assert.True(Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) >= 0);
        }
        string physical;
        await using (var command = gate.CreateCommand())
        {
            command.Transaction = tx;
            command.CommandText = "SELECT resource_description FROM sys.dm_tran_locks WHERE resource_database_id=DB_ID() AND resource_type='APPLICATION' AND request_session_id=@@SPID AND request_mode='X' AND request_status='GRANT'";
            physical = Assert.IsType<string>(await command.ExecuteScalarAsync());
        }
        var pending = start(); var paths = Array.Empty<int[]>(); var workers = Array.Empty<int>(); var held = false; var waiting = false; var bothPending = false;
        try
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(8))
            {
                await using var command = gate.CreateCommand(); command.Transaction = tx;
                command.CommandText = "SELECT session_id,blocking_session_id,wait_type FROM sys.dm_exec_requests WHERE database_id=DB_ID() AND session_id<>@@SPID;SELECT request_session_id,request_mode,request_status,resource_description FROM sys.dm_tran_locks WHERE resource_database_id=DB_ID() AND resource_type='APPLICATION';";
                var requests = new List<(int Id, int Blocker, string? Wait)>(); var locks = new List<(int Id, string Mode, string Status, string Resource)>();
                await using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync()) requests.Add((reader.GetInt16(0), reader.GetInt16(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
                    await reader.NextResultAsync();
                    while (await reader.ReadAsync()) locks.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
                }
                int[] Path(int id)
                {
                    var path = new List<int>(); var seen = new HashSet<int>();
                    for (var depth = 0; depth < 32 && seen.Add(id); depth++)
                    {
                        path.Add(id); if (id == gateSessionId) return path.ToArray();
                        var row = requests.SingleOrDefault(x => x.Id == id); if (row.Blocker <= 0) break; id = row.Blocker;
                    }
                    return [];
                }
                workers = requests.Where(x => x.Wait == "LCK_M_X" && locks.Any(l => l.Id == x.Id && l.Mode == "X" && l.Status is "WAIT" or "CONVERT"))
                    .Select(x => x.Id).Distinct().Where(id => Path(id).Length > 1).ToArray();
                paths = workers.Select(Path).ToArray();
                held = locks.Count(x => x.Id == gateSessionId && x.Mode == "X" && x.Status == "GRANT" && x.Resource == physical) == 1;
                waiting = workers.Length == 2 && paths.Length == 2;
                bothPending = pending.Length == 2 && pending.All(x => !x.IsCompleted);
                if (held && waiting && bothPending) break;
                await Task.Delay(25);
            }
            output.WriteLine(marker + " " + JsonSerializer.Serialize(new { OwnedDatabase = gate.Database, GateSessionId = gateSessionId,
                RequestedResource = resource, ExactGateApplicationResource = physical, DistinctWorkerSessionIds = workers, AcyclicGatePaths = paths,
                GateHeldBeforeRelease = held, BothActualSqlWorkersWaiting = waiting, BothTasksPendingBeforeRelease = bothPending }));
        }
        finally { await tx.RollbackAsync(); }
        var results = await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(35));
        Assert.Equal(2, workers.Distinct().Count()); Assert.Equal(2, paths.Length); Assert.True(held); Assert.True(waiting); Assert.True(bothPending);
        return results;
    }
}
