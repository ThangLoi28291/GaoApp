using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class AcbCallbackIntakeSqlTests
{
    private const string Key = "isolated-acb-intake-test";
    private const string Artifact = ".artifacts/security-issue11-acb-intake";

    [Fact]
    public async Task One_thousand_callbacks_survive_saturated_legacy_limits_failed_auth_processing_outage_and_restart()
    {
        await using var app = await Setup();
        var store = app.Stores[0];
        // This CHECK affects only this GUID-owned database. Intake remains writable, processing cannot commit.
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AcbCallbackReceipts] ADD CONSTRAINT [CK_Test_AcbProcessingPaused] CHECK ([ProcessedAtUtc] IS NULL);");
        using var client = app.Anonymous(store); client.Http.Timeout = TimeSpan.FromMinutes(2);
        using (var forbidden = await SendInvalid("wrong-key")) Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using (var limited = await SendInvalid("another-wrong-key")) Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        client.Http.DefaultRequestHeaders.Add("x-api-key", Key);

        var payloads = Enumerable.Range(0, 1000).Select(i => Payload(Guid.NewGuid(), i)).ToArray();
        var receipts = new ConcurrentDictionary<int, int>();
        var progressLock = new object(); var active = 0; var peak = 0; var acknowledged = 0;
        var clock = Stopwatch.StartNew();
        Progress("receiving");
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await Parallel.ForEachAsync(Enumerable.Range(0, 1000),
            new ParallelOptions { MaxDegreeOfParallelism = 48, CancellationToken = deadline.Token }, async (index, ct) =>
            {
                var concurrent = Interlocked.Increment(ref active);
                lock (progressLock) peak = Math.Max(peak, concurrent);
                try
                {
                    var route = (index % 3) switch { 0 => "/api/acb/webhook", 1 => "/Admin/api-callback", _ => "/api/acb/webhook/" };
                    receipts[index] = await Accept(client, route, payloads[index], ct);
                    if (Interlocked.Increment(ref acknowledged) % 200 == 0) Progress("receiving");
                }
                finally { Interlocked.Decrement(ref active); }
            });
        Assert.Equal(1000, receipts.Count); Assert.Equal(1000, receipts.Values.Distinct().Count());
        Assert.InRange(peak, 33, 48);
        await VerifyDurable(processed: false);
        var receiveSeconds = clock.Elapsed.TotalSeconds;
        Progress("received-all-before-processing");

        // Restart only this fixture's Web process. All ACKed rows must outlive that process.
        await app.RestartAsync(enableDeliveryOutbox: false);
        await VerifyDurable(processed: false);
        await Parallel.ForEachAsync(Enumerable.Range(0, 64), new ParallelOptions { MaxDegreeOfParallelism = 16 }, async (index, ct) =>
            Assert.Equal(receipts[index], await Accept(client, "/Admin/api-callback", payloads[index], ct)));
        await VerifyDurable(processed: false);
        Progress("restarted-and-duplicates-verified");

        await using (var db = app.Database.CreateTenantContext(store.StoreId))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AcbCallbackReceipts] DROP CONSTRAINT [CK_Test_AcbProcessingPaused];");
        await app.RestartAsync(enableDeliveryOutbox: false);
        // Normal production worker: existing batch size 50 / maximum four concurrent processors.
        var resume = Stopwatch.StartNew();
        var processed = 0;
        while (resume.Elapsed < TimeSpan.FromMinutes(4))
        {
            await using var db = app.Database.CreateTenantContext(store.StoreId);
            processed = await db.Set<AcbCallbackReceipt>().CountAsync(x => x.ProcessedAtUtc != null);
            if (processed == 1000) break;
            await Task.Delay(1000);
        }
        Assert.Equal(1000, processed);
        await VerifyDurable(processed: true);
        Progress("all-processed");
        await File.WriteAllTextAsync(Path.Combine(FullApplicationFixture.SourceRoot(), Artifact, "load-result.json"),
            JsonSerializer.Serialize(new { callbacks = 1000, duplicateRetries = 64, peakClientInFlight = peak,
                receiveSeconds, recoverySeconds = resume.Elapsed.TotalSeconds, durableReceipts = 1000,
                durableTransactionItems = 1000, processed = 1000, payments = 0, legacyLimits = "1 request / 1 concurrent; ObserveOnly=false" }));

        async Task<HttpResponseMessage> SendInvalid(string credential)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/acb/webhook")
                { Content = new StringContent("{invalid-json", System.Text.Encoding.UTF8, "application/json") };
            request.Headers.Add("x-api-key", credential); return await client.Http.SendAsync(request);
        }
        async Task VerifyDurable(bool processed)
        {
            await using var db = app.Database.CreateTenantContext(store.StoreId);
            Assert.Equal(1000, await db.Set<AcbCallbackReceipt>().CountAsync());
            Assert.Equal(1000, await db.Set<AcbQrNotificationItem>().CountAsync());
            Assert.Equal(processed ? 1000 : 0, await db.Set<AcbCallbackReceipt>().CountAsync(x => x.ProcessedAtUtc != null));
            Assert.Empty(await db.OrderPayments.ToListAsync());
            Assert.Equal(100, (await db.InventoryBalances.SingleAsync(x => x.ProductVariantId == store.VariantId)).OnHandQty);
        }
        void Progress(string stage)
        {
            lock (progressLock) File.WriteAllText(Path.Combine(FullApplicationFixture.SourceRoot(), Artifact, "load-progress.json"),
                JsonSerializer.Serialize(new { stage, acknowledged, peakClientInFlight = peak, elapsedSeconds = clock.Elapsed.TotalSeconds }));
        }
    }

    [Fact]
    public async Task Invalid_auth_and_failed_storage_never_ACK_and_retry_succeeds_once_after_storage_recovers()
    {
        await using var app = await Setup(); var store = app.Stores[0];
        using var client = app.Anonymous(store);
        foreach (var malformedHeader in new[] { "wrong", "wrong-again" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/api-callback")
                { Content = new StringContent("{bad", System.Text.Encoding.UTF8, "application/json") };
            request.Headers.Add("x-api-key", malformedHeader);
            using var rejected = await client.Http.SendAsync(request);
            Assert.True(rejected.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests);
        }
        client.Http.DefaultRequestHeaders.Add("x-api-key", Key);
        using (var invalid = await client.Http.PostAsync("/api/acb/webhook", new StringContent("{bad", System.Text.Encoding.UTF8, "application/json")))
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var logicalId = Guid.Parse("00000000-0000-0000-0000-00000000ffff");
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AcbCallbackReceipts] ADD CONSTRAINT [CK_Test_AcbStorageFails] CHECK ([ClientRequestId] <> '00000000-0000-0000-0000-00000000ffff');");
        var payload = Payload(logicalId, 1);
        using (var failed = await client.Http.PostAsJsonAsync("/api/acb/webhook", payload))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            Assert.Contains("INBOX_SAVE_FAILED", await failed.Content.ReadAsStringAsync());
            Assert.DoesNotContain("00000000", await failed.Content.ReadAsStringAsync());
        }
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            Assert.Empty(await db.Set<AcbCallbackReceipt>().ToListAsync());
            Assert.Empty(await db.Set<AcbQrNotificationItem>().ToListAsync());
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AcbCallbackReceipts] DROP CONSTRAINT [CK_Test_AcbStorageFails];");
        }
        var id = await Accept(client, "/api/acb/webhook", payload);
        Assert.Equal(id, await Accept(client, "/Admin/api-callback", payload));
        // The authentication marker is request-local: removing the saved key revokes the very next callback.
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
            await db.Set<StoreAcbSettings>().ExecuteUpdateAsync(x => x.SetProperty(p => p.CallbackApiKeyProtected, ""));
        using (var revoked = await client.Http.PostAsJsonAsync("/api/acb/webhook", Payload(Guid.NewGuid(), 2)))
            Assert.True(revoked.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests);
        await using var check = app.Database.CreateTenantContext(store.StoreId);
        Assert.Single(await check.Set<AcbCallbackReceipt>().ToListAsync());
        Assert.Single(await check.Set<AcbQrNotificationItem>().ToListAsync());
        Assert.Empty(await check.OrderPayments.ToListAsync());
    }

    private static async Task<FullApplicationFixture> Setup()
    {
        var app = await FullApplicationFixture.StartAsync();
        try
        {
            await app.ConfigureResourceProtectionAsync(new Dictionary<string, string> {
                ["Limits__BankIngress__CallerPerWindow"] = "1", ["Limits__BankIngress__ProcessPerWindow"] = "1",
                ["Limits__BankIngress__CallerConcurrency"] = "1", ["Limits__BankIngress__ProcessConcurrency"] = "1",
                ["Limits__BankAccepted__StorePerWindow"] = "1", ["Limits__BankAccepted__ProcessPerWindow"] = "1",
                ["Limits__BankAccepted__StoreConcurrency"] = "1", ["Limits__BankAccepted__ProcessConcurrency"] = "1",
                ["Limits__BankAccepted__ObserveOnly"] = "false" });
            var store = app.Stores[0]; int bankId;
            await using (var db = app.Database.CreateTenantContext(store.StoreId))
            {
                var bank = new StoreBankAccount { StoreId = store.StoreId, BankCode = "ACB", BankName = "TEST",
                    AccountNumber = "CALLBACK-INTAKE", AccountName = "TEST", IsActive = true };
                db.Add(bank); await db.SaveChangesAsync(); bankId = bank.Id;
            }
            using var admin = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
            using var saved = await admin.Http.PostAsync("/admin/acb/settings", new FormUrlEncodedContent(new Dictionary<string, string> {
                ["BankAccountId"] = bankId.ToString(), ["Enabled"] = "false", ["CallbackApiKey"] = Key }));
            Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
            return app;
        }
        catch { await app.DisposeAsync(); throw; }
    }

    private static JsonElement Payload(Guid id, int index) => JsonSerializer.SerializeToElement(new {
        requestTrace = "intake-" + index, requestDateTime = DateTimeOffset.UtcNow,
        requestParameters = new { masterMeta = new { clientId = "00000000-0000-0000-0000-000000000001", clientRequestId = id, checksum = "synthetic" },
            request = new { requestMeta = new { requestType = "NOTIFICATION", requestCode = "TRANSACTION_UPDATE" },
                requestParams = new { transactions = new[] { new { transactionStatus = "COMPLETED", effectiveDate = "2026-10-23", amount = 100,
                    transactionEntityAttribute = new { custom4 = "UNKNOWN-INTAKE-" + index } } },
                    pagination = new { page = 1, pageSize = 100, totalPage = 1 } } } } });

    private static async Task<int> Accept(FullApplicationFixture.Client client, string route, JsonElement payload, CancellationToken ct = default)
    {
        using var response = await client.Http.PostAsJsonAsync(route, payload, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{route}: {(int)response.StatusCode} {text}");
        using var ack = JsonDocument.Parse(text);
        Assert.Equal("00000000", ack.RootElement.GetProperty("responseStatus").GetProperty("responseCode").GetString());
        return int.Parse(ack.RootElement.GetProperty("responseBody").GetProperty("referenceCode").GetString()!);
    }
}
