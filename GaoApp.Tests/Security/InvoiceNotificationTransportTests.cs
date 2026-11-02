using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.InvoiceNotifications;
using GaoApp.Web.Services.CustomerService;
using GaoApp.Web.Services.InvoiceNotifications;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GaoApp.Tests.Security;

public sealed class InvoiceNotificationTransportTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "GaoInvoiceWireTests", Guid.NewGuid().ToString("N"));
    private readonly AssistantIntegrationTests.FixedClock clock = new();
    private readonly AssistantIntegrationTests.Monitor<InvoiceNotificationOptions> options = new(new() { Enabled = true });
    private readonly Reader reader = new();
    private readonly CustomerChannelConnection connection;
    private readonly CustomerChannelRegistry registry;
    public InvoiceNotificationTransportTests()
    {
        Directory.CreateDirectory(Path.Combine(directory, "credentials"));
        var token = Path.Combine(directory, "credentials", "token"); File.WriteAllText(token, "SYNTHETIC_BEARER_TOKEN_ONLY");
        connection = new() { Id = "zalo-invoice-test", Provider = "zalomarketing", StoreId = 7, AccountId = Guid.NewGuid().ToString("D"), BaseUrl = "https://zalo.example.test/", Enabled = true, AccessTokenFile = token };
        registry = new(new AssistantIntegrationTests.Monitor<CustomerChannelOptions>(new() { CredentialDirectory = Path.Combine(directory, "credentials"), Connections = [connection] }), new AssistantIntegrationTests.EnvironmentStub(Path.Combine(directory, "web")));
    }
    private InvoiceNotificationWorkItem Item(bool accepted = false)
    {
        var request = Guid.NewGuid();
        var binding = new InvoiceRecipientBindingSnapshot(Guid.NewGuid(), 7, 11, connection.Id, connection.Binding, Guid.Parse(connection.AccountId), Guid.NewGuid(), 42, "sdk-v1:123456789", "0398946174", 1, clock.Now.UtcDateTime, null);
        var snapshot = new InvoiceDocumentSnapshot { StoreId = 7, CustomerId = 11, SourceType = "Invoice", SourceId = 9, SourceVersion = "synthetic-v1", Kind = "OfficialPdf" };
        var file = new InvoicePreparedPart("File", null, "invoice-test.pdf", "application/pdf", reader.Bytes.Length, Convert.ToHexString(SHA256.HashData(reader.Bytes)).ToLowerInvariant(), new string('a', 64));
        var bundle = new InvoicePreparedBundle([new("Text", "Hóa đơn kiểm thử", null, null, null, null, null), file], new string('b', 64));
        return new(Guid.NewGuid(), Guid.NewGuid(), 7, 11, "Invoice", 9, snapshot.SourceVersion, "OfficialPdf", request, "gao-invoice-" + request.ToString("N"), accepted ? "Accepted" : "Dispatching", clock.Now.UtcDateTime, binding, snapshot, bundle, accepted ? request : null, 1);
    }
    private InvoiceNotificationTransport Transport(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) => new(new HttpClient(new Handler(handler)), registry, reader, options, clock);
    private HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK, string? profile = null)
    {
        var result = new HttpResponseMessage(status) { Content = JsonContent.Create(body) };
        result.Headers.Add("X-ZaloMarketing-Sender-Profile-Id", profile ?? connection.AccountId); return result;
    }
    private object Accepted(InvoiceNotificationWorkItem item) => new { jobId = item.RequestId, status = "Pending", createdAtUtc = clock.Now, idempotencyReplayed = false, conversationId = item.Binding.ConversationId, replyToSequence = 42, partCount = 2, partsUrl = $"/api/v1/jobs/{item.RequestId}/parts" };
    private JsonElement Parts(InvoiceNotificationWorkItem item, string first = "Sent", string second = "Sent", string delivery = "Sent") => JsonSerializer.SerializeToElement(new
    {
        jobId = item.ZaloJobId, senderProfileId = item.Binding.SenderProfileId,
        deliveries = new[] { new { deliveryId = Guid.NewGuid(), recipient = item.Binding.NativePeer, status = delivery,
            parts = item.Prepared!.Parts.Select((p, i) => new { index = i + 1, kind = p.Kind, state = i == 0 ? first : second, text = p.Text, fileName = p.FileName, byteLength = p.ByteLength,
                sha256 = p.Sha256?.ToUpperInvariant(), receiptId = (i == 0 ? first : second) == "Sent" ? (i + 100).ToString() : null, attemptId = (Guid?)null, detail = (string?)null }).ToArray() } }
    });

    [Fact]
    public async Task Lost_post_acknowledgement_reuses_exact_request_key_manifest_and_original_bytes_and_202_is_only_accepted()
    {
        var item = Item(); var calls = new List<(string Key, string Manifest, string Bytes)>();
        var transport = Transport(async (r, ct) => {
            Assert.Equal(HttpMethod.Post, r.Method); Assert.EndsWith($"api/v1/inbox/conversations/{item.Binding.ConversationId}/replies/bundles", r.RequestUri!.ToString());
            Assert.Equal("Bearer SYNTHETIC_BEARER_TOKEN_ONLY", r.Headers.Authorization!.ToString());
            var form = Assert.IsType<MultipartFormDataContent>(r.Content); var manifest = form.Single(p => p.Headers.ContentDisposition!.Name!.Trim('"') == "Manifest");
            var file = form.Single(p => p.Headers.ContentDisposition!.Name!.Trim('"') == "asset-1");
            Assert.Equal("application/pdf", file.Headers.ContentType!.MediaType); Assert.Equal("invoice-test.pdf", file.Headers.ContentDisposition!.FileName!.Trim('"'));
            calls.Add((r.Headers.GetValues("Idempotency-Key").Single(), await manifest.ReadAsStringAsync(ct), Convert.ToHexString(await file.ReadAsByteArrayAsync(ct))));
            if (calls.Count == 1) throw new HttpRequestException("SYNTHETIC acknowledgement lost");
            return Json(Accepted(item), HttpStatusCode.Accepted);
        });
        Assert.Equal("Retry", (await transport.SendAsync(item, item.Prepared!, _ => Task.FromResult(true), default)).State);
        Assert.Equal("Accepted", (await transport.SendAsync(item with { Attempts = 2 }, item.Prepared!, _ => Task.FromResult(true), default)).State);
        Assert.Equal(2, calls.Count); Assert.Equal(calls[0], calls[1]);
        using var manifestJson = JsonDocument.Parse(calls[0].Manifest); Assert.Equal(item.RequestId, manifestJson.RootElement.GetProperty("requestId").GetGuid());
        Assert.Equal("Text", manifestJson.RootElement.GetProperty("parts")[0].GetProperty("kind").GetString());
        Assert.Equal(Convert.ToHexString(reader.Bytes), calls[0].Bytes);
    }
    [Fact]
    public async Task Revocation_after_materializing_assets_is_handled_without_HTTP_or_second_settlement()
    {
        var item = Item(); var calls = 0;
        var transport = Transport((_, _) => { calls++; throw new Exception(); });
        var result = await transport.SendAsync(item, item.Prepared!, _ => { Assert.Equal(1, reader.Reads); return Task.FromResult(false); }, default);
        Assert.Equal("AuthorizationChanged", result.State); Assert.Equal(0, calls);
    }
    [Theory]
    [InlineData("hash")][InlineData("customer")][InlineData("filename")][InlineData("mime")][InlineData("storage")]
    public async Task Private_artifact_or_recipient_mismatch_is_rejected_before_HTTP(string fault)
    {
        var item = Item(); var parts = item.Prepared!.Parts.ToArray();
        if (fault == "hash") reader.Bytes = [1, 2, 3];
        if (fault == "customer") item = item with { CustomerId = 12 };
        if (fault == "filename") parts[1] = parts[1] with { FileName = "../invoice.pdf" };
        if (fault == "mime") parts[1] = parts[1] with { ContentType = "text/plain" };
        if (fault == "storage") parts[1] = parts[1] with { StorageKey = "/private/path" };
        item = item with { Prepared = item.Prepared with { Parts = parts } }; var calls = 0;
        var transport = Transport((_, _) => { calls++; throw new Exception(); });
        Assert.Equal("NeedsReview", (await transport.SendAsync(item, item.Prepared, _ => Task.FromResult(true), default)).State); Assert.Equal(0, calls);
    }
    [Fact]
    public async Task Artifact_not_ready_never_queues_a_partial_bundle()
    {
        var item = Item(); reader.NotReady = true; var calls = 0;
        var transport = Transport((_, _) => { calls++; throw new Exception(); });
        var result = await transport.SendAsync(item, item.Prepared!, _ => Task.FromResult(true), default);
        Assert.Equal("Retry", result.State); Assert.Equal("artifact_not_ready", result.Code); Assert.Equal(0, calls);
    }
    [Theory][InlineData(202, false)][InlineData(200, true)]
    public async Task Missing_profile_attestation_or_wrong_acceptance_status_never_becomes_sent(int status, bool correctProfile)
    {
        var item = Item(); var transport = Transport((_, _) => Task.FromResult(Json(Accepted(item), (HttpStatusCode)status, correctProfile ? connection.AccountId : Guid.NewGuid().ToString("D"))));
        Assert.Equal("NeedsReview", (await transport.SendAsync(item, item.Prepared!, _ => Task.FromResult(true), default)).State);
    }
    [Fact]
    public async Task Expired_uncertain_request_is_not_submitted_under_a_new_key()
    {
        var item = Item() with { Attempts = 2, CreatedAtUtc = clock.Now.AddHours(-24).UtcDateTime }; var calls = 0;
        var transport = Transport((_, _) => { calls++; throw new Exception(); });
        Assert.Equal("NeedsReview", (await transport.SendAsync(item, item.Prepared!, _ => Task.FromResult(true), default)).State); Assert.Equal(0, calls);
    }
    [Theory]
    [InlineData("Sent", "Sent", "Sent", "Sent")]
    [InlineData("Sent", "Uncertain", "NeedsReview", "NeedsReview")]
    [InlineData("Sent", "Cancelled", "PartiallySent", "PartiallySent")]
    [InlineData("Pending", "Pending", "Pending", "Accepted")]
    [InlineData("FailedBeforeSend", "Cancelled", "Failed", "Failed")]
    [InlineData("Cancelled", "Cancelled", "Skipped", "Cancelled")]
    public async Task Parts_poll_checks_exact_recipient_and_per_part_evidence_before_classifying(string first, string second, string delivery, string expected)
    {
        var item = Item(true); var calls = 0;
        var transport = Transport((r, _) => { calls++; Assert.Equal(HttpMethod.Get, r.Method); Assert.EndsWith($"api/v1/jobs/{item.ZaloJobId}/parts", r.RequestUri!.ToString()); return Task.FromResult(Json(Parts(item, first, second, delivery))); });
        var result = await transport.PollAsync(item, item.Prepared!, default); Assert.Equal(expected, result.State); Assert.Equal(1, calls);
        Assert.DoesNotContain("Hóa đơn", result.PartsOutcomeJson); // outcome stores whitelisted metadata, not chat text
    }
    [Theory][InlineData("recipient")][InlineData("profile")][InlineData("job")][InlineData("filename")][InlineData("index")][InlineData("hash")]
    public async Task Foreign_job_customer_or_part_evidence_is_never_persisted_as_our_outcome(string fault)
    {
        var item = Item(true); var node = System.Text.Json.Nodes.JsonNode.Parse(Parts(item).GetRawText())!;
        if (fault == "recipient") node["deliveries"]![0]!["recipient"] = "sdk-v1:999999";
        if (fault == "profile") node["senderProfileId"] = Guid.NewGuid();
        if (fault == "job") node["jobId"] = Guid.NewGuid();
        if (fault == "filename") node["deliveries"]![0]!["parts"]![1]!["fileName"] = "FOREIGN-CUSTOMER.pdf";
        if (fault == "index") node["deliveries"]![0]!["parts"]![1]!["index"] = 1;
        if (fault == "hash") node["deliveries"]![0]!["parts"]![1]!["sha256"] = new string('c', 64);
        var transport = Transport((_, _) => Task.FromResult(Json(node)));
        var result = await transport.PollAsync(item, item.Prepared!, default); Assert.Equal("NeedsReview", result.State); Assert.Null(result.PartsOutcomeJson);
    }
    [Fact]
    public async Task Known_job_never_posts_and_poll_network_loss_keeps_same_job()
    {
        var item = Item(true); var calls = 0; var transport = Transport((r, _) => { calls++; Assert.Equal(HttpMethod.Get, r.Method); throw new HttpRequestException(); });
        Assert.Equal("NeedsReview", (await transport.SendAsync(item, item.Prepared!, _ => Task.FromResult(true), default)).State);
        var result = await transport.PollAsync(item, item.Prepared!, default); Assert.Equal("Accepted", result.State); Assert.Equal(item.ZaloJobId, result.JobId); Assert.Equal(1, calls);
    }
    [Fact]
    public async Task Recipient_source_uses_current_authenticated_SDK_anchor_and_namespaces_only_the_verified_peer()
    {
        var item = Item(); var requestCount = 0;
        using var http = new HttpClient(new Handler((r, _) => { requestCount++; Assert.Null(r.RequestUri!.Query == "" ? null : r.RequestUri.Query); return Task.FromResult(Json(Detail(item))); }));
        var source = new CustomerInvoiceRecipientSource(new(http, registry, clock), registry, clock);
        var proof = await source.ResolveAsync(connection, item.Binding.ConversationId, 42, default);
        Assert.Equal("sdk-v1:123456789", proof.NativePeer); Assert.Equal(item.Binding.SenderProfileId, proof.SenderProfileId); Assert.Equal(1, requestCount);
    }
    [Theory][InlineData("profile")][InlineData("peer")][InlineData("sequence")][InlineData("transport")][InlineData("outgoing")][InlineData("future")]
    public async Task Recipient_source_rejects_unproven_or_stale_anchors(string fault)
    {
        var item = Item(); var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Detail(item)))!;
        if (fault == "profile") node["recipientEvidence"]!["senderProfileId"] = Guid.NewGuid();
        if (fault == "peer") node["recipientEvidence"]!["nativePeer"] = "sdk-v1:123456789";
        if (fault == "sequence") node["recipientEvidence"]!["latestIncomingSequence"] = 41;
        if (fault == "transport") node["recipientEvidence"]!["transport"] = "Playwright";
        if (fault == "outgoing") node["messages"]![0]!["isOutgoing"] = true;
        if (fault == "future") node["recipientEvidence"]!["observedAtUtc"] = clock.Now.AddHours(1);
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json(node))));
        var source = new CustomerInvoiceRecipientSource(new(http, registry, clock), registry, clock);
        await Assert.ThrowsAnyAsync<Exception>(() => source.ResolveAsync(connection, item.Binding.ConversationId, 42, default));
    }
    private object Detail(InvoiceNotificationWorkItem item) => new { id = item.Binding.ConversationId, displayName = "SYNTHETIC customer", kind = "Direct", messages = new[] { new { sequence = 42L, text = "SYNTHETIC", observedAtUtc = clock.Now, isOutgoing = false, senderId = "123456789" } },
        recipientEvidence = new { transport = "Sdk", kind = "Direct", senderProfileId = item.Binding.SenderProfileId, conversationId = item.Binding.ConversationId, nativePeer = "123456789", latestIncomingSequence = 42L, observedAtUtc = clock.Now } };
    [Fact]
    public async Task Manual_recipient_discovery_and_inspection_work_with_SDK_history_Waiting_without_enabling_AI_intake()
    {
        var item = Item(); var calls = 0;
        using var http = new HttpClient(new Handler((r, _) => {
            calls++;
            return Task.FromResult(Json(r.RequestUri!.AbsolutePath.EndsWith("/conversations") ? new { sync = new { enabled = true, status = "Waiting" }, conversations = new[] { new { id = item.Binding.ConversationId, displayName = "SYNTHETIC customer", conversationKind = "Direct", lastObservedAtUtc = clock.Now } } } : Detail(item)));
        }));
        var source = new CustomerInvoiceRecipientSource(new(http, registry, clock), registry, clock);
        var candidate = Assert.Single(await source.ListAsync(connection, "SYNTHETIC", default)); Assert.Equal(item.Binding.ConversationId, candidate.ConversationId);
        var inspected = await source.InspectAsync(connection, candidate.ConversationId, default);
        Assert.Equal(42, inspected.Evidence.IncomingSequence); Assert.Equal("sdk-v1:123456789", inspected.Evidence.NativePeer); Assert.Equal(2, calls);
    }
    [Theory][InlineData(403)][InlineData(500)]
    public async Task Manual_recipient_source_failure_is_a_safe_application_error_without_upstream_payload(int status)
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("SYNTHETIC private upstream diagnostic") })));
        var source = new CustomerInvoiceRecipientSource(new(http, registry, clock), registry, clock);
        var error = await Assert.ThrowsAsync<GaoApp.Web.Services.Assistant.AssistantException>(() => source.ListAsync(connection, "", default));
        Assert.Equal("invoice_recipient_source_unavailable", error.Code); Assert.DoesNotContain("upstream", error.Message);
    }
    [Fact]
    public async Task Dispatcher_does_not_submit_when_disabled_and_core_has_no_accepted_jobs()
    {
        options.CurrentValue.Enabled = false; var box = new Outbox(Item()) { NoClaim = true }; var build = new Builder();
        var dispatcher = new InvoiceNotificationDispatcher(box, build, Transport((_, _) => throw new Exception()), registry, options);
        Assert.False(await dispatcher.ProcessNextAsync(default)); Assert.Equal(1, box.Claims); Assert.Equal(0, build.Calls);
    }
    [Fact]
    public async Task Dispatcher_rechecks_source_after_preparation_and_does_not_settle_an_already_blocked_lease_twice()
    {
        var box = new Outbox(Item() with { Prepared = null }); box.Authorizations.Enqueue(true); box.Authorizations.Enqueue(false); box.Validations.Enqueue(false); var build = new Builder { Bundle = Item().Prepared! }; var calls = 0;
        var dispatcher = new InvoiceNotificationDispatcher(box, build, Transport((_, _) => { calls++; throw new Exception(); }), registry, options);
        Assert.True(await dispatcher.ProcessNextAsync(default)); Assert.Equal(1, box.Saves); Assert.Equal(1, box.Revalidations); Assert.Equal(0, calls); Assert.Null(box.Settlement);
    }
    [Fact]
    public async Task Dispatcher_polls_an_accepted_job_without_rebuilding_or_source_revalidation()
    {
        var item = Item(true); var box = new Outbox(item); var build = new Builder();
        var dispatcher = new InvoiceNotificationDispatcher(box, build, Transport((r, _) => { Assert.Equal(HttpMethod.Get, r.Method); return Task.FromResult(Json(Parts(item))); }), registry, options);
        Assert.True(await dispatcher.ProcessNextAsync(default)); Assert.Equal("Sent", box.Settlement); Assert.Equal(0, build.Calls); Assert.Equal(0, box.Revalidations);
    }
    [Fact]
    public async Task Dispatcher_records_accepted_job_but_never_reports_HTTP202_as_sent()
    {
        var item = Item(); var box = new Outbox(item);
        var dispatcher = new InvoiceNotificationDispatcher(box, new Builder(), Transport((_, _) => Task.FromResult(Json(Accepted(item), HttpStatusCode.Accepted))), registry, options);
        await dispatcher.ProcessNextAsync(default); Assert.Equal("Accepted", box.Settlement); Assert.Equal(item.RequestId, box.AcceptedJob);
    }
    [Fact]
    public async Task Dispatcher_durably_settles_uncertain_HTTP_even_after_caller_cancellation()
    {
        var item = Item(); var box = new Outbox(item); using var stop = new CancellationTokenSource();
        var dispatcher = new InvoiceNotificationDispatcher(box, new Builder(), Transport((_, _) => { stop.Cancel(); throw new OperationCanceledException(stop.Token); }), registry, options);
        await dispatcher.ProcessNextAsync(stop.Token); Assert.Equal("Retry", box.Settlement); Assert.False(box.SettlementToken.CanBeCanceled); Assert.Equal(item.IdempotencyKey, box.Item.IdempotencyKey);
    }
    [Fact]
    public async Task Accepted_job_is_cancelled_after_revocation_even_when_new_sends_are_disabled()
    {
        var item = Item(true); options.CurrentValue.Enabled = false; var box = new Outbox(item) { Authorized = false }; var calls = new List<string>();
        var dispatcher = new InvoiceNotificationDispatcher(box, new Builder(), Transport((r, _) => {
            calls.Add(r.Method + " " + r.RequestUri!.AbsolutePath);
            if (r.Method == HttpMethod.Post) { Assert.EndsWith("/cancel", r.RequestUri.AbsolutePath); return Task.FromResult(Json(new { jobId = item.RequestId, status = "Cancelled", stateChanged = true })); }
            return Task.FromResult(Json(calls.Count == 1 ? Parts(item, "Pending", "Pending", "Pending") : Parts(item, "Cancelled", "Cancelled", "Skipped")));
        }), registry, options);
        await dispatcher.ProcessNextAsync(default); Assert.Equal("Cancelled", box.Settlement); Assert.Equal(3, calls.Count); Assert.DoesNotContain(calls, s => s.Contains("/replies/"));
    }
    [Fact]
    public async Task Running_revoked_job_is_stopped_after_cancel_conflict_and_previous_sent_part_is_preserved()
    {
        var item = Item(true); var box = new Outbox(item) { Authorized = false }; var calls = new List<string>();
        var dispatcher = new InvoiceNotificationDispatcher(box, new Builder(), Transport((r, _) => {
            calls.Add(r.RequestUri!.AbsolutePath);
            if (r.RequestUri.AbsolutePath.EndsWith("/cancel")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict));
            if (r.RequestUri.AbsolutePath.EndsWith("/stop")) return Task.FromResult(Json(new { jobId = item.RequestId, status = "Stopped", stateChanged = true }));
            return Task.FromResult(Json(calls.Count == 1 ? Parts(item, "Sent", "Pending", "Pending") : Parts(item, "Sent", "Cancelled", "PartiallySent")));
        }), registry, options);
        await dispatcher.ProcessNextAsync(default); Assert.Equal("PartiallySent", box.Settlement); Assert.Equal(4, calls.Count);
    }
    [Fact]
    public async Task Unknown_post_is_discovered_and_adopted_before_cancelling_exact_same_job_without_a_second_bundle_post()
    {
        var item = Item() with { Attempts = 2 }; var box = new Outbox(item) { Authorized = false }; var calls = new List<string>();
        var known = item with { ZaloJobId = item.RequestId };
        var dispatcher = new InvoiceNotificationDispatcher(box, new Builder(), Transport((r, _) => {
            calls.Add(r.Method + " " + r.RequestUri!.AbsolutePath);
            if (r.Method == HttpMethod.Post) { Assert.EndsWith("/cancel", r.RequestUri.AbsolutePath); Assert.Equal(item.RequestId, box.Item.ZaloJobId); return Task.FromResult(Json(new { jobId = item.RequestId, status = "Cancelled", stateChanged = true })); }
            return Task.FromResult(Json(calls.Count < 3 ? Parts(known, "Pending", "Pending", "Pending") : Parts(known, "Cancelled", "Cancelled", "Skipped")));
        }), registry, options);
        await dispatcher.ProcessNextAsync(default); Assert.Equal(item.RequestId, box.Item.ZaloJobId); Assert.Equal("Cancelled", box.Settlement); Assert.Single(calls.Where(s => s.StartsWith("POST"))); Assert.Equal(0, box.Revalidations);
    }
    [Fact]
    public async Task Discovery_for_a_foreign_recipient_is_not_adopted_or_cancelled()
    {
        var item = Item() with { Attempts = 2 }; var box = new Outbox(item) { Authorized = false }; var calls = 0;
        var known = item with { ZaloJobId = item.RequestId }; var node = System.Text.Json.Nodes.JsonNode.Parse(Parts(known).GetRawText())!; node["deliveries"]![0]!["recipient"] = "sdk-v1:777";
        var dispatcher = new InvoiceNotificationDispatcher(box, new Builder(), Transport((r, _) => { calls++; Assert.Equal(HttpMethod.Get, r.Method); return Task.FromResult(Json(node)); }), registry, options);
        await dispatcher.ProcessNextAsync(default); Assert.Null(box.Item.ZaloJobId); Assert.Equal("NeedsReview", box.Settlement); Assert.Equal(1, calls);
    }
    [Fact]
    public async Task Revoked_unknown_post_with_initial404_keeps_probing_to_find_and_cancel_a_late_acceptance()
    {
        var item = Item() with { Attempts = 2 }; var box = new Outbox(item) { Authorized = false }; var calls = 0;
        var known = item with { ZaloJobId = item.RequestId };
        var dispatcher = new InvoiceNotificationDispatcher(box, new Builder(), Transport((r, _) => {
            calls++;
            if (calls == 1) { Assert.Equal(HttpMethod.Get, r.Method); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)); }
            if (r.Method == HttpMethod.Post) { Assert.EndsWith("/cancel", r.RequestUri!.AbsolutePath); return Task.FromResult(Json(new { jobId = item.RequestId, status = "Cancelled", stateChanged = true })); }
            return Task.FromResult(Json(calls < 4 ? Parts(known, "Pending", "Pending", "Pending") : Parts(known, "Cancelled", "Cancelled", "Skipped")));
        }), registry, options);
        await dispatcher.ProcessNextAsync(default); Assert.Equal("Retry", box.Settlement); Assert.Null(box.Item.ZaloJobId);
        await dispatcher.ProcessNextAsync(default); Assert.Equal("Cancelled", box.Settlement); Assert.Equal(item.RequestId, box.Item.ZaloJobId);
    }
    [Fact]
    public async Task Missing_control_scope_requires_review_and_never_submits_a_new_bundle()
    {
        var item = Item(true); var posts = 0;
        var transport = Transport((r, _) => { if (r.Method == HttpMethod.Post) { posts++; Assert.EndsWith("/cancel", r.RequestUri!.AbsolutePath); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)); } return Task.FromResult(Json(Parts(item, "Pending", "Pending", "Pending"))); });
        var result = await transport.CancelAndPollAsync(item, item.Prepared!, default); Assert.Equal("NeedsReview", result.State); Assert.Equal("job_control_http_403", result.Code); Assert.Equal(1, posts);
    }
    [Fact]
    public async Task Real_loopback_HTTP_carries_private_original_file_and_manifest_to_only_the_existing_bundle_endpoint()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = directory, EnvironmentName = "Development" });
        builder.Configuration["AllowedHosts"] = "127.0.0.1"; builder.Logging.ClearProviders();
        await using var app = builder.Build(); app.Urls.Add("http://127.0.0.1:0");
        InvoiceNotificationWorkItem? item = null; var calls = 0;
        app.MapPost("/api/v1/inbox/conversations/{conversationId:guid}/replies/bundles", async (HttpRequest request, Guid conversationId) => {
            calls++; Assert.Equal(item!.Binding.ConversationId, conversationId); Assert.Equal(item.IdempotencyKey, request.Headers["Idempotency-Key"]);
            Assert.Equal("Bearer SYNTHETIC_BEARER_TOKEN_ONLY", request.Headers.Authorization);
            var form = await request.ReadFormAsync(); Assert.Single(form.Files); Assert.Equal("invoice-test.pdf", form.Files[0].FileName);
            using var file = new MemoryStream(); await form.Files[0].CopyToAsync(file); Assert.Equal(reader.Bytes, file.ToArray());
            using var manifest = JsonDocument.Parse(form["Manifest"].ToString()); Assert.Equal(item.RequestId, manifest.RootElement.GetProperty("requestId").GetGuid());
            request.HttpContext.Response.Headers["X-ZaloMarketing-Sender-Profile-Id"] = connection.AccountId;
            return Results.Json(Accepted(item), statusCode: 202);
        });
        await app.StartAsync();
        try
        {
            connection.BaseUrl = app.Urls.Single(); item = Item();
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false });
            var transport = new InvoiceNotificationTransport(http, registry, reader, options, clock);
            var result = await transport.SendAsync(item, item.Prepared!, _ => Task.FromResult(true), default);
            Assert.True(result.State == "Accepted", $"State={result.State}; code={result.Code}; local calls={calls}"); Assert.Equal(1, calls);
        }
        finally { await app.StopAsync(); }
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => handler(request, ct); }
    private sealed class Reader : IInvoicePreparedAssetReader
    {
        public byte[] Bytes = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<</Type/Catalog>>endobj\n%%EOF\n"); public int Reads; public bool NotReady;
        public Task<byte[]> ReadAsync(InvoiceNotificationWorkItem item, InvoicePreparedPart part, CancellationToken ct) { Reads++; if (NotReady) throw new InvoiceAssetNotReadyException(); return Task.FromResult(Bytes); }
    }
    private sealed class Builder : IInvoiceNotificationBundleBuilder
    { public int Calls; public InvoicePreparedBundle? Bundle; public Task<InvoicePreparedBundle> BuildAsync(InvoiceDocumentSnapshot snapshot, CancellationToken ct) { Calls++; return Task.FromResult(Bundle ?? throw new InvalidOperationException()); } }
    private sealed class Outbox(InvoiceNotificationWorkItem initial) : IInvoiceNotificationOutbox
    {
        public InvoiceNotificationWorkItem Item = initial; public int Claims, Saves, Revalidations; public bool NoClaim; public bool Authorized = true; public string? Settlement; public Guid? AcceptedJob; public CancellationToken SettlementToken; public Queue<bool> Validations = new(); public Queue<bool> Authorizations = new();
        public Task<InvoiceNotificationWorkItem?> ClaimAsync(CancellationToken ct) { Claims++; return Task.FromResult<InvoiceNotificationWorkItem?>(NoClaim ? null : Item); }
        public Task<bool> IsAuthorizedAsync(InvoiceNotificationWorkItem item, string currentConnectionBinding, CancellationToken ct) => Task.FromResult(Authorizations.Count > 0 ? Authorizations.Dequeue() : Authorized);
        public Task<InvoiceNotificationWorkItem> AttachDiscoveredAcceptedAsync(InvoiceNotificationWorkItem item, Guid job, CancellationToken ct) { Item = item with { ZaloJobId = job }; return Task.FromResult(Item); }
        public Task<InvoiceNotificationWorkItem> SavePreparedAsync(InvoiceNotificationWorkItem item, InvoicePreparedBundle bundle, CancellationToken ct) { Saves++; Item = item with { Prepared = bundle }; return Task.FromResult(Item); }
        public Task<bool> RevalidateAsync(InvoiceNotificationWorkItem item, string currentConnectionBinding, CancellationToken ct) { Revalidations++; return Task.FromResult(Validations.Count == 0 || Validations.Dequeue()); }
        public Task RecordAcceptedAsync(InvoiceNotificationWorkItem item, Guid job, CancellationToken ct) { AcceptedJob = job; return Set("Accepted", ct); }
        public Task RecordRetryAsync(InvoiceNotificationWorkItem item, string detail, TimeSpan delay, CancellationToken ct) => Set("Retry", ct);
        public Task RecordReviewAsync(InvoiceNotificationWorkItem item, string detail, CancellationToken ct) => Set("NeedsReview", ct);
        public Task RecordPartsOutcomeAsync(InvoiceNotificationWorkItem item, string state, string partsOutcomeJson, CancellationToken ct) => Set(state, ct);
        private Task Set(string state, CancellationToken ct) { Settlement = state; SettlementToken = ct; return Task.CompletedTask; }
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
