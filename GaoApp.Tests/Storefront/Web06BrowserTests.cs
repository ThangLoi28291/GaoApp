using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Services.Banking;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb06"), Trait("Category", "StorefrontWeb06"), Trait("Browser", "Chromium")]
public sealed class Web06BrowserTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_native_admin_mobile_operations_lost_response_partial_stock_cash_sale_and_private_guest_delivery_work_in_browser(bool national)
    {
        var c = await Web06Case.CreateAsync(fixture, national: national); await Web06FinanceSupport.ConfigureInvoiceOwnersAsync(c.Checkout);
        var terminal = await Web06FinanceSupport.OwnOpenShiftAsync(c); var output = Web06Support.Artifact("browser/" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
        var token = Web04Support.GuestToken(c.Placed); var startedAt = DateTime.UtcNow; string log;
        await using (var admin = await Web06AdminHost.StartAsync(c, terminal))
        await using (var guest = await Web04PublicProcess.StartAsync(c.Checkout))
        {
            log = guest.LogPath;
            var input = new { adminUrl = admin.Origin, publicUrl = guest.BaseUrl, output, userId = c.Seed.UserId, terminalId = terminal, orderId = c.OrderId,
                guestLink = c.Placed.GuestLink, code = c.Placed.Code, courierId = c.CourierId, national };
            var script = Path.Combine(Web03Support.Root(), "GaoApp.Tests.Browser/storefront-web06.browser.cjs");
            var start = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(Web03Support.Root(), "GaoApp.Tests.Browser"), UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(script); using var process = new Process { StartInfo = start }; Assert.True(process.Start());
            await File.WriteAllTextAsync(Path.Combine(output, "owned-browser-process.json"), JsonSerializer.Serialize(new { process.Id, StartedAtUtc = process.StartTime.ToUniversalTime(),
                Method = nameof(Actual_native_admin_mobile_operations_lost_response_partial_stock_cash_sale_and_private_guest_delivery_work_in_browser), National = national,
                StoreId = c.StoreId, OrderId = c.OrderId, RuntimeRoot = c.Checkout.Directory, Database = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(c.Seed.Database.ConnectionString).InitialCatalog,
                ScriptSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(script))), GuestTokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))) }));
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(input)); process.StandardInput.Close();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8)); var exit = process.WaitForExitAsync(timeout.Token);
            var lost = false; var partial = false; var dispatched = false; var cash = false; var completed = false; var maintenance = false;
            AppDbContext? busyDb = null; IDbContextTransaction? busyTx = null; var busyHeld = false; var busyReleased = false; var busyRetried = false; var busyBlocked = false; var busyOwner = 0; var busyWaiter = 0; var busyAt = DateTime.MinValue;
            var busyDatabase = 0; var busyResource = $"GaoApp:Storefront:Order:{c.StoreId}:{c.OrderId}"; var busyPolls = new List<object>();
            try
            {
                while (!exit.IsCompleted)
                {
                    if (!lost && File.Exists(Path.Combine(output, "checkpoint-lost-prepare")))
                    {
                        await using var db = c.Context(); Assert.Single(await db.DeliveryOrders.ToListAsync()); Assert.Single(await db.Set<StorefrontFulfillmentWork>().ToListAsync());
                        Assert.Single(await db.Set<StorefrontCommandReceipt>().Where(x => x.Operation == "StartPreparation").ToListAsync()); Assert.Empty(await db.DeliveryJournalEntries.ToListAsync());
                        lost = true; await Ack(output, "checkpoint-lost-verified");
                    }
                    if (!busyHeld && File.Exists(Path.Combine(output, "checkpoint-busy-lock-needed")))
                    {
                        busyDb = c.Context(); busyTx = await busyDb.Database.BeginTransactionAsync();
                        await GlobalBankReceiptLedger.LockAsync(busyDb, busyResource, timeout.Token);
                        busyOwner = ((SqlConnection)busyDb.Database.GetDbConnection()).ServerProcessId;
                        busyDatabase = await busyDb.Database.SqlQueryRaw<int>("SELECT CONVERT(int,DB_ID()) AS [Value]").SingleAsync(timeout.Token);
                        Assert.Equal("Exclusive", await busyDb.Database.SqlQueryRaw<string>("SELECT APPLOCK_MODE(N'public',{0},N'Transaction') AS [Value]", busyResource).SingleAsync(timeout.Token));
                        busyAt = DateTime.UtcNow; busyHeld = true;
                        await Ack(output, "checkpoint-busy-lock-acquired");
                    }
                    if (busyHeld && !busyReleased && !busyBlocked)
                    {
                        var candidates = admin.OwnedSqlSessions.Where(x => x.Operation == "pick" && x.AtUtc >= busyAt).Select(x => x.SessionId).Distinct().ToArray();
                        if (candidates.Length > 0)
                        {
                            await using var observer = c.Context(); var requests = new List<OwnedBusyRequest>();
                            foreach (var candidate in candidates)
                                requests.AddRange(await observer.Database.SqlQueryRaw<OwnedBusyRequest>("SELECT CONVERT(int,session_id) AS SessionId,CONVERT(int,blocking_session_id) AS BlockingSessionId,CONVERT(int,database_id) AS ResourceDatabaseId,status AS Status,wait_type AS WaitType,wait_resource AS WaitResource FROM sys.dm_exec_requests WHERE session_id={0} AND database_id={1}", candidate, busyDatabase).ToListAsync(timeout.Token));
                            busyPolls.Add(new { AtUtc = DateTime.UtcNow, Candidates = candidates, Requests = requests });
                            var blocked = requests.FirstOrDefault(x => candidates.Contains(x.SessionId) && x.BlockingSessionId == busyOwner && x.ResourceDatabaseId == busyDatabase);
                            if (blocked is not null) { busyWaiter = blocked.SessionId; busyBlocked = true; }
                        }
                    }
                    if (!busyReleased && File.Exists(Path.Combine(output, "checkpoint-busy-response")))
                    {
                        await File.WriteAllTextAsync(Path.Combine(output, "busy-sql-observation-trace.json"), JsonSerializer.Serialize(new { OwnerSession = busyOwner, ResourceDatabaseId = busyDatabase, Resource = busyResource, ActualSqlBlockingObserved = busyBlocked, Polls = busyPolls }));
                        Assert.True(busyBlocked, "Actual browser request must be observed blocked on the owned order-lock session.");
                        await busyTx!.RollbackAsync(); await busyTx.DisposeAsync(); busyTx = null; await busyDb!.DisposeAsync(); busyDb = null; busyReleased = true;
                        await File.WriteAllTextAsync(Path.Combine(output, "busy-sql-blocking-proof.json"), JsonSerializer.Serialize(new { OwnerSession = busyOwner, WaiterSession = busyWaiter, ResourceDatabaseId = busyDatabase, Resource = busyResource, ActualSqlBlockingObserved = true, HeldAtUtc = busyAt, ReleasedAtUtc = DateTime.UtcNow }));
                        await Ack(output, "checkpoint-busy-lock-released");
                    }
                    if (!busyRetried && File.Exists(Path.Combine(output, "checkpoint-busy-retried")))
                    {
                        await using var db = c.Context(); Assert.Single(await db.Set<StorefrontCommandReceipt>().Where(x => x.Operation == "RecordPicking").ToListAsync());
                        Assert.Equal(4m, (await db.DeliveryPickingLines.SingleAsync()).ReportedQuantity); busyRetried = true; await Ack(output, "checkpoint-busy-once-verified");
                    }
                    if (!partial && File.Exists(Path.Combine(output, "checkpoint-partial-rejected")))
                    {
                        await using var db = c.Context(); Assert.Empty(await db.DeliveryJournalEntries.ToListAsync()); Assert.Equal(10m, (await db.InventoryBalances.SingleAsync()).OnHandQty);
                        Assert.Equal(4m, (await db.InventoryBalances.SingleAsync()).ReservedQty); Assert.Equal(3m, (await db.DeliveryPickingLines.SingleAsync()).ReportedQuantity);
                        Assert.Equal(200_000m, (await db.StorefrontOrders.SingleAsync()).GoodsAmount); partial = true; await Ack(output, "checkpoint-partial-verified");
                    }
                    if (!dispatched && File.Exists(Path.Combine(output, "checkpoint-dispatched")))
                    {
                        await using var db = c.Context(); Assert.Empty(await db.Orders.ToListAsync()); Assert.Equal(6m, (await db.InventoryBalances.SingleAsync()).OnHandQty);
                        Assert.Equal(0m, (await db.InventoryBalances.SingleAsync()).ReservedQty); Assert.Equal(40_000m, (await db.DeliveryJournalEntries.SingleAsync()).CostAmount);
                        Assert.Equal(InventoryTransactionType.DeliveryDispatchOut, (await db.InventoryTransactions.SingleAsync(x => x.ReferenceType == InventoryReferenceType.Delivery)).TransactionType);
                        dispatched = true; await Ack(output, "checkpoint-dispatch-verified");
                    }
                    if (!cash && File.Exists(Path.Combine(output, "checkpoint-cash-needed")))
                    {
                        await using (var db = c.Context()) { var summary = await Web06FinanceSupport.Settlement(c, db, terminal).GetAsync(c.OrderId); Assert.Equal(200_000m, summary.CustomerPaid); Assert.Equal(0m, summary.ActualStoreReceived); Assert.Equal(200_000m, summary.CollectorOutstanding); Assert.Empty(await db.Orders.ToListAsync()); }
                        var id = await Web06FinanceSupport.ActualCashInAsync(c, terminal, 200_000m); await File.WriteAllTextAsync(Path.Combine(output, "checkpoint-cash.json"), JsonSerializer.Serialize(new { id }));
                        cash = true; await Ack(output, "checkpoint-cash-written");
                    }
                    if (!completed && File.Exists(Path.Combine(output, "checkpoint-completed")))
                    {
                        await using var db = c.Context(); Assert.Single(await db.Orders.ToListAsync()); Assert.Single(await db.InvoiceHeads.ToListAsync()); Assert.Equal(StorefrontOrderPhase.Completed, (await db.StorefrontOrders.SingleAsync()).Phase);
                        Assert.Equal(1, await db.InventoryTransactions.CountAsync(x => x.ReferenceType == InventoryReferenceType.Delivery)); Assert.Equal(40_000m, await db.DeliveryDispatchCostFragments.SumAsync(x => x.CostAmount));
                        Assert.Equal(200_000m, (await db.POSShifts.SingleAsync()).CashInTotal); Assert.Equal(0m, (await db.POSShifts.SingleAsync()).CashSalesTotal); Assert.Single(await db.POSShiftCashTransactions.ToListAsync());
                        completed = true; await Ack(output, "checkpoint-sale-verified");
                    }
                    if (!maintenance && File.Exists(Path.Combine(output, "checkpoint-maintenance")))
                    {
                        await using var db = c.Context(); await db.StorefrontSettings.ExecuteUpdateAsync(x => x.SetProperty(s => s.IsEnabled, false)); maintenance = true; await Ack(output, "checkpoint-maintenance-closed");
                    }
                    await Task.WhenAny(exit, Task.Delay(100, timeout.Token));
                }
                await exit; var error = await stderr; var text = await stdout;
                Assert.DoesNotContain(token, error); Assert.DoesNotContain(token, text); await File.WriteAllTextAsync(Path.Combine(output, "node.stdout.log"), text); await File.WriteAllTextAsync(Path.Combine(output, "node.stderr.log"), error);
                Assert.True(process.ExitCode == 0, "WEB06 actual browser failed: " + error + " Evidence: " + output); Assert.True(lost && busyBlocked && busyReleased && busyRetried && partial && dispatched && cash && completed && maintenance);
                using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "browser-results.json")));
                Assert.Equal(10, report.RootElement.GetProperty("results").GetArrayLength()); Assert.All(report.RootElement.GetProperty("results").EnumerateArray(), row => Assert.True(row.GetProperty("passed").GetBoolean()));
                await File.WriteAllTextAsync(Path.Combine(output, "canonical-method-link.json"), JsonSerializer.Serialize(new { Method = nameof(Actual_native_admin_mobile_operations_lost_response_partial_stock_cash_sale_and_private_guest_delivery_work_in_browser),
                    National = national, StartedAtUtc = startedAt, FinishedAtUtc = DateTime.UtcNow, StoreId = c.StoreId, OrderId = c.OrderId, RuntimeRoot = c.Checkout.Directory, NodePid = process.Id,
                    Basis = "Unique GUID output passed to this owned node over stdin; actual method checkpoints verified before acknowledgments", AdminBodyOnly = true, GuestSeparateProcess = true }));
            }
            finally
            {
                if (busyTx is not null) { await busyTx.RollbackAsync(); await busyTx.DisposeAsync(); }
                if (busyDb is not null) await busyDb.DisposeAsync();
                if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            }
        }
        Assert.DoesNotContain(token, await File.ReadAllTextAsync(log));
    }
    private sealed class OwnedBusyRequest
    {
        public int SessionId { get; set; }
        public int BlockingSessionId { get; set; }
        public int ResourceDatabaseId { get; set; }
        public string? Status { get; set; }
        public string? WaitType { get; set; }
        public string? WaitResource { get; set; }
    }
    private static Task Ack(string output, string name) => File.WriteAllTextAsync(Path.Combine(output, name), "Owned actual fixture assertion completed; no secrets.");
}
