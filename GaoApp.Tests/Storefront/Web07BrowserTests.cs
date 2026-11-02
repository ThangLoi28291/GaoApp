using GaoApp.Domain.Entities;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Services.Banking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb07"), Trait("Category", "StorefrontWeb07"), Trait("Browser", "Chromium")]
public sealed class Web07BrowserTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_guest_capability_navigation_cancellation_recall_and_admin_existing_debit_refund_survive_unknown_responses_on_five_widths(bool recall)
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture, prepaidQr: true); var physical = c.Physical!;
        if (recall) { await physical.PrepareAsync(); await physical.PickAllAsync(); await physical.DispatchAsync(); }
        var bRequest = c.Checkout.Request(1m); bRequest.PaymentMethod = StorefrontPaymentMethod.Cod;
        var bQuote = await c.Checkout.QuoteAsync(bRequest); var bResult = await c.Checkout.PlaceAsync(Web04Case.PlaceRequest(bQuote));
        Assert.Equal(StorefrontPlacementStatus.Placed, bResult.Status); var b = bResult.Order!;
        int bId; await using (var db = c.Context()) bId = await db.StorefrontOrders.Where(x => x.Code == b.Code).Select(x => x.Id).SingleAsync();
        var tokens = new[] { c.Token, Web04Support.GuestToken(b) };
        var output = Web07Support.Artifact("browser/" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
        var startedAt = DateTime.UtcNow;
        await using var admin = await Web07AdminHost.StartAsync(c);
        await using var guest = await Web07PublicHost.StartAsync(c);
        guest.ProtectCapability(tokens[1]);
        var input = new { adminUrl = admin.Origin, publicUrl = guest.Origin, output, userId = c.Seed.UserId,
            orderId = c.OrderId, aLink = c.Placed.GuestLink, aCode = c.Placed.Code, bLink = b.GuestLink, bCode = b.Code,
            ownerId = c.Seed.LegalEntityId, bankAccountId = c.Seed.BankAccountId, recall };
        var script = Path.Combine(Web03Support.Root(), "GaoApp.Tests.Browser/storefront-web07.browser.cjs");
        var start = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(Web03Support.Root(), "GaoApp.Tests.Browser"),
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(script); using var process = new Process { StartInfo = start }; Assert.True(process.Start());
        await File.WriteAllTextAsync(Path.Combine(output, "owned-browser-process.json"), JsonSerializer.Serialize(new
        {
            process.Id, StartedAtUtc = process.StartTime.ToUniversalTime(), Method = nameof(Actual_guest_capability_navigation_cancellation_recall_and_admin_existing_debit_refund_survive_unknown_responses_on_five_widths),
            Recall = recall, c.StoreId, c.OrderId, SiblingOrderId = bId, RuntimeRoot = c.Directory,
            Database = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(c.Seed.Database.ConnectionString).InitialCatalog,
            ScriptSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(script))),
            CapabilityHashes = tokens.Select(x => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(x)))).ToArray(), NoRawCapabilityPersisted = true
        }));
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(input)); process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8)); var exit = process.WaitForExitAsync(timeout.Token);
        var aChecked = false; var bChecked = false; var returned = !recall; var debitReady = false; var approved = false; var payoutChecked = false; var maintenance = false;
        Web07RecordedDebit? debit = null; string? flowBeforeClaim = null; int refundId = 0;
        AppDbContext? ownerDb = null; IDbContextTransaction? ownerTx = null;
        var busyHeld = false; var busyReleased = false; var busyObserved = false; var ownerSpid = 0; var waiterSpid = 0; var databaseId = 0; var busyAt = DateTime.MinValue;
        var resource = $"GaoApp:Storefront:Order:{c.StoreId}:{c.OrderId}"; var polls = new List<object>();
        try
        {
            while (!exit.IsCompleted)
            {
                if (!aChecked && Exists("checkpoint-a-committed"))
                {
                    await using var db = c.Context(); var request = Assert.Single(await db.Set<StorefrontCancellationRequest>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
                    var parent = await db.StorefrontOrders.SingleAsync(x => x.Id == c.OrderId);
                    Assert.Equal(recall ? StorefrontOrderPhase.Shipping : StorefrontOrderPhase.Cancelled, parent.Phase);
                    Assert.Equal(recall, (await Web07Support.Cancellation(c, db).GetAsync(c.OrderId)).PendingRequestId == request.Id);
                    Assert.Empty(await db.DeliveryReturnedCostFragments.ToListAsync());
                    Assert.Equal(recall ? 0m : 230_000m, await db.Set<StorefrontRefundFunding>().Where(x => x.StorefrontOrderId == c.OrderId).SumAsync(x => x.Amount));
                    aChecked = true; await Ack("checkpoint-a-verified");
                }
                if (!bChecked && Exists("checkpoint-b-lost"))
                {
                    await using var db = c.Context(); Assert.Equal(StorefrontOrderPhase.Cancelled, (await db.StorefrontOrders.SingleAsync(x => x.Id == bId)).Phase);
                    Assert.Single(await db.Set<StorefrontCancellationRequest>().Where(x => x.StorefrontOrderId == bId).ToListAsync());
                    Assert.Empty(await db.Set<StorefrontRefund>().Where(x => x.StorefrontOrderId == bId).ToListAsync());
                    Assert.Empty(await db.InventoryReservations.Where(x => x.StorefrontOrderId == bId && x.Status == InventoryReservationStatus.Active).ToListAsync());
                    bChecked = true; await Ack("checkpoint-b-verified");
                }
                if (!returned && Exists("checkpoint-return-needed"))
                {
                    await using (var db = c.Context())
                    {
                        var state = await Web07Support.Cancellation(c, db).GetAsync(c.OrderId); Assert.True(state.ReturnAuthorised); Assert.Null(state.PendingRequestId);
                        Assert.Equal(StorefrontOrderPhase.Shipping, state.Phase); Assert.Empty(await db.Set<StorefrontRefundFunding>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
                    }
                    var actualReturn = await physical.ReturnRequestAsync();
                    await using (var db = c.Context()) await physical.Service(db, [StorefrontPermissions.ViewOrders, StorefrontPermissions.ReceiveReturns]).ReceiveReturnAsync(c.OrderId, actualReturn);
                    await using (var db = c.Context()) { Assert.Equal(4m, await db.DeliveryReturnedCostFragments.SumAsync(x => x.BaseQuantity)); Assert.Equal(40_000m, await db.DeliveryReturnedCostFragments.SumAsync(x => x.CostAmount)); }
                    returned = true; await Ack("checkpoint-return-verified");
                }
                if (!debitReady && Exists("checkpoint-debit-needed"))
                {
                    await using (var db = c.Context())
                    {
                        var parent = await db.StorefrontOrders.SingleAsync(x => x.Id == c.OrderId); Assert.Equal(StorefrontOrderPhase.Cancelled, parent.Phase);
                        var refund = Assert.Single(await db.Set<StorefrontRefund>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync()); refundId = refund.Id;
                        Assert.Equal(230_000m, refund.Amount); Assert.Equal(200_000m, refund.GoodsAmount); Assert.Equal(30_000m, refund.ShippingAmount);
                        Assert.Equal(StorefrontRefundState.Requested, refund.State); Assert.Empty(await db.Set<StorefrontRefundEvidence>().Where(x => x.RefundId == refundId).ToListAsync());
                        Assert.NotEqual(6, (await db.Set<StorefrontQrProcessing>().SingleAsync()).State);
                        Assert.Equal(10m, (await db.InventoryBalances.SingleAsync()).OnHandQty); Assert.Equal(0m, (await db.InventoryBalances.SingleAsync()).ReservedQty);
                    }
                    debit = recall ? await Web07RecordedDebit.BankAsync(c, 230_000m) : await Web07RecordedDebit.CashAsync(c, 230_000m);
                    flowBeforeClaim = await Web07RefundExecutionTests.FlowAsync(c);
                    await File.WriteAllTextAsync(Path.Combine(output, "checkpoint-debit.json"), JsonSerializer.Serialize(new
                    { kind = (int)debit.Source.Kind, id = debit.Source.Id, externalId = debit.ExternalReference, executedAt = debit.ActualExecutionAt }));
                    debitReady = true; await Ack("checkpoint-debit-ready");
                }
                if (!busyHeld && Exists("checkpoint-busy-lock-needed"))
                {
                    ownerDb = c.Context(); ownerTx = await ownerDb.Database.BeginTransactionAsync(timeout.Token);
                    await GlobalBankReceiptLedger.LockAsync(ownerDb, resource, timeout.Token);
                    ownerSpid = await ownerDb.Database.SqlQueryRaw<int>("SELECT CONVERT(int,@@SPID) AS [Value]").SingleAsync(timeout.Token);
                    databaseId = await ownerDb.Database.SqlQueryRaw<int>("SELECT CONVERT(int,DB_ID()) AS [Value]").SingleAsync(timeout.Token);
                    Assert.True(ownerSpid > 0); Assert.Equal("Exclusive", await ownerDb.Database.SqlQueryRaw<string>("SELECT APPLOCK_MODE(N'public',{0},N'Transaction') AS [Value]", resource).SingleAsync(timeout.Token));
                    busyAt = DateTime.UtcNow; busyHeld = true; await Ack("checkpoint-busy-lock-acquired");
                }
                if (busyHeld && !busyReleased && !busyObserved)
                {
                    var candidates = admin.OwnedSqlSessions.Where(x => x.Operation == "approve" && x.AtUtc >= busyAt).Select(x => x.SessionId).Distinct().ToArray();
                    await using var observer = c.Context(); var requests = new List<BlockedRequest>();
                    foreach (var candidate in candidates)
                        requests.AddRange(await observer.Database.SqlQueryRaw<BlockedRequest>("SELECT CONVERT(int,session_id) AS SessionId,CONVERT(int,blocking_session_id) AS BlockingSessionId,CONVERT(int,database_id) AS ResourceDatabaseId,status AS Status,wait_type AS WaitType,wait_resource AS WaitResource FROM sys.dm_exec_requests WHERE session_id={0} AND database_id={1}", candidate, databaseId).ToListAsync(timeout.Token));
                    polls.Add(new { AtUtc = DateTime.UtcNow, Candidates = candidates, Requests = requests });
                    var row = requests.FirstOrDefault(x => candidates.Contains(x.SessionId) && x.BlockingSessionId == ownerSpid && x.ResourceDatabaseId == databaseId);
                    if (row is not null) { waiterSpid = row.SessionId; busyObserved = true; }
                }
                if (!busyReleased && Exists("checkpoint-busy-response"))
                {
                    await File.WriteAllTextAsync(Path.Combine(output, "busy-sql-observation-trace.json"), JsonSerializer.Serialize(new { OwnerSpid = ownerSpid, ResourceDatabaseId = databaseId, Resource = resource, ActualSqlBlockingObserved = busyObserved, Polls = polls }));
                    Assert.True(busyObserved, "Actual owned HTTP connection must be observed waiting on the exact owned order-lock session/database.");
                    await ownerTx!.RollbackAsync(); await ownerTx.DisposeAsync(); ownerTx = null; await ownerDb!.DisposeAsync(); ownerDb = null;
                    busyReleased = true; await File.WriteAllTextAsync(Path.Combine(output, "busy-sql-blocking-proof.json"), JsonSerializer.Serialize(new { OwnerSpid = ownerSpid, WaiterSpid = waiterSpid, ResourceDatabaseId = databaseId, Resource = resource, HeldAtUtc = busyAt, ReleasedAtUtc = DateTime.UtcNow }));
                    await Ack("checkpoint-busy-lock-released");
                }
                if (!approved && Exists("checkpoint-approved"))
                {
                    await using var db = c.Context(); var refund = await db.Set<StorefrontRefund>().SingleAsync(x => x.Id == refundId);
                    Assert.Equal(StorefrontRefundState.Approved, refund.State); Assert.Empty(await db.Set<StorefrontRefundEvidence>().Where(x => x.RefundId == refundId).ToListAsync());
                    Assert.Single(await db.Set<StorefrontRefundDecision>().Where(x => x.RefundId == refundId && x.Kind == StorefrontRefundDecisionKind.Approved).ToListAsync());
                    Assert.Equal(flowBeforeClaim, await Web07RefundExecutionTests.FlowAsync(c)); approved = true; await Ack("checkpoint-approved-verified");
                }
                if (!payoutChecked && Exists("checkpoint-payout-lost"))
                {
                    await using var db = c.Context(); var refund = await db.Set<StorefrontRefund>().SingleAsync(x => x.Id == refundId);
                    Assert.Equal(StorefrontRefundState.Executed, refund.State); var proof = Assert.Single(await db.Set<StorefrontRefundEvidence>().Where(x => x.RefundId == refundId).ToListAsync());
                    Assert.Equal(230_000m, proof.Amount); Assert.Equal(c.Seed.UserId, proof.ConfirmedByUserId);
                    Assert.Equal(230_000m, await db.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId).SumAsync(x => x.Amount));
                    Assert.Equal(flowBeforeClaim, await Web07RefundExecutionTests.FlowAsync(c)); Assert.Empty(await db.Orders.ToListAsync()); Assert.Empty(await db.OrderPayments.ToListAsync());
                    Assert.Equal(recall ? 4m : 0m, await db.DeliveryReturnedCostFragments.SumAsync(x => x.BaseQuantity));
                    payoutChecked = true; await Ack("checkpoint-payout-verified");
                }
                if (!maintenance && Exists("checkpoint-maintenance"))
                {
                    await using var db = c.Context(); await db.StorefrontSettings.ExecuteUpdateAsync(x => x.SetProperty(s => s.IsEnabled, false));
                    maintenance = true; await Ack("checkpoint-maintenance-ready");
                }
                await Task.WhenAny(exit, Task.Delay(100, timeout.Token));
            }
            await exit;
            Assert.True(process.ExitCode == 0, "WEB07 browser failed; see sanitized evidence: " + output);
            Assert.True(aChecked && bChecked && returned && debitReady && busyObserved && busyReleased && approved && payoutChecked && maintenance);
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "browser-results.json")));
            Assert.Equal(10, report.RootElement.GetProperty("results").GetArrayLength());
            Assert.All(report.RootElement.GetProperty("results").EnumerateArray(), x => Assert.True(x.GetProperty("passed").GetBoolean()));
            foreach (var relative in new[] { "GaoApp.Storefront.Web/wwwroot/assets/cancellation.js", "GaoApp.Storefront.Web/wwwroot/assets/cancellation.css", "GaoApp.Web/wwwroot/Admin/js/storefront-refunds.js", "GaoApp.Web/wwwroot/Admin/css/storefront-refunds.css" })
            {
                var assetPath = "/" + relative[(relative.IndexOf("wwwroot/", StringComparison.Ordinal) + 8)..];
                var expected = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(Web03Support.Root(), relative))));
                Assert.Contains(report.RootElement.GetProperty("served").EnumerateArray(), x => x.GetProperty("path").GetString() == assetPath && string.Equals(x.GetProperty("sha256").GetString(), expected, StringComparison.OrdinalIgnoreCase));
            }
            await File.WriteAllTextAsync(Path.Combine(output, "canonical-method-link.json"), JsonSerializer.Serialize(new
            {
                Method = nameof(Actual_guest_capability_navigation_cancellation_recall_and_admin_existing_debit_refund_survive_unknown_responses_on_five_widths), Recall = recall,
                StartedAtUtc = startedAt, FinishedAtUtc = DateTime.UtcNow, c.StoreId, c.OrderId, SiblingOrderId = bId, NodePid = process.Id,
                Basis = "Unique owned output passed over stdin to this method's node process; actual SQL checkpoints verified before acknowledgments", AdminFeatureBodyOnly = true, GuestFullSeparateHost = true
            }));
        }
        finally
        {
            if (ownerTx is not null) { await ownerTx.RollbackAsync(); await ownerTx.DisposeAsync(); }
            if (ownerDb is not null) await ownerDb.DisposeAsync();
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            var text = await stdout; var error = await stderr;
            foreach (var token in tokens) { Web07Support.NoCapability(text, token, "owned node stdout"); Web07Support.NoCapability(error, token, "owned node stderr"); }
            await File.WriteAllTextAsync(Path.Combine(output, "node.stdout.log"), text); await File.WriteAllTextAsync(Path.Combine(output, "node.stderr.log"), error);
        }
        await using (var check = c.Context())
        {
            Assert.Single(await check.Set<StorefrontRefundEvidence>().Where(x => x.RefundId == refundId).ToListAsync());
            Assert.Equal(10m, (await check.InventoryBalances.SingleAsync()).OnHandQty); Assert.Equal(0m, (await check.InventoryBalances.SingleAsync()).ReservedQty);
            Assert.Equal(flowBeforeClaim, await Web07RefundExecutionTests.FlowAsync(c));
        }
        // The process writes its final log on disposal; the caller's final cleanup
        // also audits every reused public-host lineage before deleting private keys.
        bool Exists(string name) => File.Exists(Path.Combine(output, name));
        Task Ack(string name) => File.WriteAllTextAsync(Path.Combine(output, name), "Actual owned fixture assertion completed; no secrets.");
    }
    private sealed class BlockedRequest
    {
        public int SessionId { get; set; }
        public int BlockingSessionId { get; set; }
        public int ResourceDatabaseId { get; set; }
        public string? Status { get; set; }
        public string? WaitType { get; set; }
        public string? WaitResource { get; set; }
    }
}
