using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Application.InvoiceNotifications;
using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Services.CustomerService;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Services.InvoiceNotifications;

public sealed record InvoiceNotificationTransportResult(string State, Guid? JobId = null, string? Code = null, string? PartsOutcomeJson = null);

/// <summary>One HTTP attempt only. Every retry reuses the immutable outbox request and idempotency key.</summary>
public sealed class InvoiceNotificationTransport(HttpClient http, CustomerChannelRegistry registry,
    IInvoicePreparedAssetReader assets, IOptionsMonitor<InvoiceNotificationOptions> options, TimeProvider clock)
{
    public async Task<InvoiceNotificationTransportResult> SendAsync(InvoiceNotificationWorkItem item, InvoicePreparedBundle bundle,
        Func<CancellationToken, Task<bool>> finalRevalidate, CancellationToken ct)
    {
        if (item.ZaloJobId is not null) return new("NeedsReview", Code: "accepted_job_must_only_poll");
        try
        {
            if (item.Attempts > 1 && clock.GetUtcNow() - Utc(item.CreatedAtUtc) >= TimeSpan.FromHours(23)) return Review("idempotency_window_expired");
            var connection = Connection(item, sending: true);
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(registry.ZaloOrigin(connection), $"api/v1/inbox/conversations/{item.Binding.ConversationId:D}/replies/bundles"));
            request.Content = await ContentAsync(item, bundle, ct);
            request.Headers.Authorization = new("Bearer", registry.Secret(connection.AccessTokenFile));
            request.Headers.Add("Idempotency-Key", item.IdempotencyKey);
            // The source, recipient revocation and immutable binding are checked after private bytes are ready.
            // This check must be nonmutating: a prior unknown POST may need discovery/cancellation under the same lease.
            if (!await finalRevalidate(ct)) return new("AuthorizationChanged", Code: "source_or_recipient_changed");
            var latest = Connection(item, sending: true);
            if (latest.Binding != connection.Binding || latest.AccessTokenFile != connection.AccessTokenFile) return Review("connection_changed_before_submit");
            var (status, body) = await JsonAsync(request, item.Binding.SenderProfileId, ct);
            if (status != HttpStatusCode.Accepted || body.GetProperty("jobId").GetGuid() != item.RequestId ||
                body.GetProperty("conversationId").GetGuid() != item.Binding.ConversationId ||
                body.GetProperty("replyToSequence").GetInt64() != item.Binding.IncomingSequence || body.GetProperty("partCount").GetInt32() != bundle.Parts.Count ||
                body.GetProperty("status").GetString() is not ("Pending" or "Running" or "Completed" or "Failed" or "Stopped" or "Cancelled")) throw new JsonException();
            // Even an acceptance replay with status Completed needs the independently verified parts read.
            return new("Accepted", item.RequestId);
        }
        catch (InvoiceAssetNotReadyException) { return new("Retry", Code: "artifact_not_ready"); }
        catch (IOException) { return new("Retry", Code: "artifact_not_ready"); }
        catch (HttpStatusFailure e) { return e.Status is 408 or 429 or >= 500 ? new("Retry", Code: "zalo_http_" + e.Status) : Review("zalo_http_" + e.Status); }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException) { return new("Retry", Code: "submit_outcome_unconfirmed"); }
        catch (Exception e) when (e is JsonException or InvalidDataException or InvalidOperationException or ArgumentException or KeyNotFoundException or AssistantException or UnauthorizedAccessException)
        { return Review(e is InvalidDataException ? "prepared_asset_or_bundle_invalid" : "submit_contract_or_binding_invalid"); }
    }

    public async Task<InvoiceNotificationTransportResult> PollAsync(InvoiceNotificationWorkItem item, InvoicePreparedBundle bundle, CancellationToken ct)
    {
        if (item.ZaloJobId is not { } job || job == Guid.Empty || job != item.RequestId) return Review("accepted_job_identity_invalid");
        try
        {
            var connection = Connection(item, sending: false);
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(registry.ZaloOrigin(connection), $"api/v1/jobs/{job:D}/parts"));
            request.Headers.Authorization = new("Bearer", registry.Secret(connection.AccessTokenFile));
            var (status, body) = await JsonAsync(request, item.Binding.SenderProfileId, ct);
            if (status != HttpStatusCode.OK) throw new JsonException();
            return ParseParts(item, bundle, body);
        }
        catch (HttpStatusFailure e) { return e.Status is 408 or 429 or >= 500 ? new("Accepted", job, "poll_unavailable") : Review("poll_http_" + e.Status); }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException) { return new("Accepted", job, "poll_unavailable"); }
        catch (Exception e) when (e is JsonException or InvalidDataException or InvalidOperationException or ArgumentException or KeyNotFoundException or AssistantException)
        { return Review("parts_contract_or_binding_invalid"); }
    }

    public async Task<InvoiceNotificationTransportResult> CancelAndPollAsync(InvoiceNotificationWorkItem item, InvoicePreparedBundle bundle, CancellationToken ct)
    {
        // First attest the exact existing job/profile/recipient/parts. Never control an unproven job.
        var observed = await PollAsync(item, bundle, ct);
        if (observed.State is "Sent" or "PartiallySent" or "Failed" or "Cancelled" || observed.PartsOutcomeJson is null) return observed;
        var job = item.ZaloJobId!.Value;
        try
        {
            var connection = Connection(item, sending: false);
            async Task Control(string command, string expectedStatus)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(registry.ZaloOrigin(connection), $"api/v1/jobs/{job:D}/{command}"));
                request.Headers.Authorization = new("Bearer", registry.Secret(connection.AccessTokenFile));
                // Existing control API has no profile header. The preceding parts read supplies profile attestation.
                var (status, body) = await JsonAsync(request, item.Binding.SenderProfileId, ct, requireProfileHeader: false);
                if (status != HttpStatusCode.OK || body.GetProperty("jobId").GetGuid() != job || body.GetProperty("status").GetString() != expectedStatus) throw new JsonException();
                _ = body.GetProperty("stateChanged").GetBoolean();
            }
            try { await Control("cancel", "Cancelled"); }
            catch (HttpStatusFailure e) when (e.Status == 409)
            {
                try { await Control("stop", "Stopped"); }
                catch (HttpStatusFailure conflict) when (conflict.Status == 409) { /* It may already be terminal; attest the actual parts again. */ }
            }
            return await PollAsync(item, bundle, ct);
        }
        catch (HttpStatusFailure e) { return e.Status is 408 or 429 or >= 500 ? new("Accepted", job, "job_control_unconfirmed", observed.PartsOutcomeJson) : Review("job_control_http_" + e.Status); }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException) { return new("Accepted", job, "job_control_unconfirmed", observed.PartsOutcomeJson); }
        catch (Exception e) when (e is JsonException or InvalidDataException or InvalidOperationException or ArgumentException or KeyNotFoundException or AssistantException)
        { return Review("job_control_contract_or_binding_invalid"); }
    }

    private CustomerChannelConnection Connection(InvoiceNotificationWorkItem item, bool sending)
    {
        var b = item.Binding;
        if (item.Id == Guid.Empty || item.RequestId == Guid.Empty || item.LeaseToken == Guid.Empty || item.StoreId <= 0 || item.CustomerId <= 0 ||
            item.StoreId != b.StoreId || item.CustomerId != b.CustomerId || item.Snapshot.StoreId != item.StoreId || item.Snapshot.CustomerId != item.CustomerId ||
            item.Snapshot.SourceVersion != item.SourceVersion || b.Id == Guid.Empty || b.Version <= 0 || b.SenderProfileId == Guid.Empty || b.ConversationId == Guid.Empty || b.IncomingSequence <= 0 ||
            b.Phone != "0398946174" || !Regex.IsMatch(b.NativePeer, @"\Asdk-v1:[1-9][0-9]{0,31}\z") ||
            item.IdempotencyKey.Length is < 1 or > 200 || item.IdempotencyKey.Any(c => c < 33 || c > 126)) throw new InvalidDataException();
        var c = registry.Get(b.ConnectionId);
        if (c.Provider != "zalomarketing" || c.StoreId != item.StoreId || c.Binding != b.ConnectionBinding || !Guid.TryParse(c.AccountId, out var profile) || profile != b.SenderProfileId ||
            sending && (!c.Enabled || b.RevokedAtUtc is not null || !options.CurrentValue.Enabled || options.CurrentValue.PilotPhone != "0398946174")) throw new InvalidDataException();
        return c;
    }

    private async Task<MultipartFormDataContent> ContentAsync(InvoiceNotificationWorkItem item, InvoicePreparedBundle bundle, CancellationToken ct)
    {
        if (item.Prepared is null || item.Prepared.Fingerprint != bundle.Fingerprint || bundle.Parts.Count is < 1 or > 10 ||
            !Regex.IsMatch(bundle.Fingerprint, @"\A[0-9a-fA-F]{64}\z")) throw new InvalidDataException();
        var form = new MultipartFormDataContent();
        try
        {
            var manifest = new List<object>(); long total = 0;
            for (var i = 0; i < bundle.Parts.Count; i++)
            {
                var part = bundle.Parts[i];
                if (!item.Prepared.Parts.SequenceEqual(bundle.Parts)) throw new InvalidDataException();
                if (part.Kind == "Text")
                {
                    if (string.IsNullOrWhiteSpace(part.Text) || part.Text.Length > 8000 || part.Text.EnumerateRunes().Take(4001).Count() > 4000 ||
                        part.FileName is not null || part.StorageKey is not null || part.ContentType is not null || part.ByteLength is not null || part.Sha256 is not null) throw new InvalidDataException();
                    manifest.Add(new { kind = "Text", text = part.Text }); continue;
                }
                ValidateAsset(part);
                total = checked(total + part.ByteLength!.Value);
                if (total > 50L * 1024 * 1024) throw new InvalidDataException();
                var bytes = await assets.ReadAsync(item, part, ct);
                if (bytes.LongLength != part.ByteLength || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(part.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
                var key = "asset-" + i.ToString(CultureInfo.InvariantCulture);
                var file = new ByteArrayContent(bytes); file.Headers.ContentType = new MediaTypeHeaderValue(part.ContentType!);
                form.Add(file, key, part.FileName!); manifest.Add(new { kind = part.Kind, fileKey = key });
            }
            form.Add(new StringContent(JsonSerializer.Serialize(new { requestId = item.RequestId, replyToSequence = item.Binding.IncomingSequence, parts = manifest }), Encoding.UTF8, "application/json"), "Manifest");
            return form;
        }
        catch { form.Dispose(); throw; }
    }

    private static void ValidateAsset(InvoicePreparedPart p)
    {
        if (p.Kind is not ("Image" or "File") || p.Text is not null || p.FileName is null || p.FileName.Length is < 1 or > 180 ||
            p.FileName is "." or ".." || p.FileName.Any(c => char.IsControl(c) || "/\\:\"<>|?*".Contains(c)) || p.FileName.EndsWith(' ') || p.FileName.EndsWith('.') ||
            p.StorageKey is null || !Regex.IsMatch(p.StorageKey, @"\A[0-9a-f]{64}\z") || p.Sha256 is null || !Regex.IsMatch(p.Sha256, @"\A[0-9a-fA-F]{64}\z") || p.ByteLength is not > 0) throw new InvalidDataException();
        var expected = Path.GetExtension(p.FileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".pdf" => "application/pdf", ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document", ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ".xml" => "application/xml", ".zip" => "application/zip", _ => null
        };
        if (expected is null || expected != p.ContentType || p.Kind == "Image" != expected.StartsWith("image/", StringComparison.Ordinal) ||
            p.ByteLength > (p.Kind == "Image" ? 10L : 20L) * 1024 * 1024) throw new InvalidDataException();
    }

    public static InvoiceNotificationTransportResult ParseParts(InvoiceNotificationWorkItem item, InvoicePreparedBundle bundle, JsonElement body)
    {
        Unique(body);
        if (body.GetProperty("jobId").GetGuid() != item.ZaloJobId || body.GetProperty("senderProfileId").GetGuid() != item.Binding.SenderProfileId) throw new JsonException();
        var deliveries = body.GetProperty("deliveries"); if (deliveries.GetArrayLength() != 1) throw new JsonException();
        var d = deliveries[0];
        if (d.GetProperty("deliveryId").GetGuid() == Guid.Empty || d.GetProperty("recipient").GetString() != item.Binding.NativePeer) throw new JsonException();
        var deliveryStatus = d.GetProperty("status").GetString();
        if (deliveryStatus is not ("Pending" or "Sending" or "Sent" or "NotFound" or "Failed" or "NeedsReview" or "Skipped" or "PartiallySent")) throw new JsonException();
        var parts = d.GetProperty("parts");
        if (bundle.Parts.Count is < 1 or > 10 || parts.GetArrayLength() != bundle.Parts.Count) throw new JsonException();
        var states = new List<string>(); var projected = new List<object>();
        for (var i = 0; i < bundle.Parts.Count; i++)
        {
            var actual = parts[i]; var expected = bundle.Parts[i];
            if (actual.GetProperty("index").GetInt32() != i + 1 || actual.GetProperty("kind").GetString() != expected.Kind ||
                Optional(actual, "text") != expected.Text || Optional(actual, "fileName") != expected.FileName ||
                OptionalLong(actual, "byteLength") != expected.ByteLength || !string.Equals(Optional(actual, "sha256"), expected.Sha256, StringComparison.OrdinalIgnoreCase)) throw new JsonException();
            var state = actual.GetProperty("state").GetString();
            if (state is not ("Pending" or "Dispatching" or "Sent" or "FailedBeforeSend" or "Uncertain" or "Cancelled")) throw new JsonException();
            var receipt = Optional(actual, "receiptId");
            if (receipt is not null && !Regex.IsMatch(receipt, @"\A[1-9][0-9]{0,127}\z")) throw new JsonException();
            // Human confirmation may establish Sent without a native receipt; unknown is never promoted here.
            if (receipt is not null && state != "Sent") throw new JsonException();
            states.Add(state); projected.Add(new { index = i + 1, kind = expected.Kind, state, expected.FileName, expected.ByteLength, expected.Sha256, receiptId = receipt });
        }
        var anySent = states.Contains("Sent"); var allSent = states.All(s => s == "Sent");
        var pending = states.Any(s => s is "Pending" or "Dispatching");
        string outcome;
        if (states.Contains("Uncertain") || deliveryStatus == "NeedsReview") outcome = "NeedsReview";
        else if (allSent && deliveryStatus == "Sent") outcome = "Sent";
        else if (allSent || deliveryStatus == "Sent") throw new JsonException();
        else if (pending && deliveryStatus is "Pending" or "Sending") outcome = "Accepted";
        else if (pending) throw new JsonException();
        else if (anySent && deliveryStatus == "PartiallySent") outcome = "PartiallySent";
        else if (!anySent && deliveryStatus is "Failed" or "NotFound") outcome = "Failed";
        else if (!anySent && deliveryStatus == "Skipped" && states.All(s => s == "Cancelled")) outcome = "Cancelled";
        else throw new JsonException();
        return new(outcome, item.ZaloJobId, PartsOutcomeJson: JsonSerializer.Serialize(new { jobId = item.ZaloJobId, senderProfileId = item.Binding.SenderProfileId,
            deliveryId = d.GetProperty("deliveryId").GetGuid(), recipient = item.Binding.NativePeer, status = deliveryStatus, parts = projected }));
    }
    private async Task<(HttpStatusCode Status, JsonElement Body)> JsonAsync(HttpRequestMessage request, Guid profile, CancellationToken ct, bool requireProfileHeader = true)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(45));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token);
        if (!response.IsSuccessStatusCode) throw new HttpStatusFailure((int)response.StatusCode);
        if (requireProfileHeader && (!response.Headers.TryGetValues("X-ZaloMarketing-Sender-Profile-Id", out var headers) || headers.Count() != 1 || !Guid.TryParseExact(headers.Single(), "D", out var actual) || actual != profile)) throw new JsonException();
        await response.Content.LoadIntoBufferAsync(1024 * 1024).WaitAsync(budget.Token);
        using var json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(budget.Token), new JsonDocumentOptions { MaxDepth = 32 });
        Unique(json.RootElement); return (response.StatusCode, json.RootElement.Clone());
    }
    private static string? Optional(JsonElement value, string key) => !value.TryGetProperty(key, out var p) || p.ValueKind == JsonValueKind.Null ? null : p.GetString();
    private static long? OptionalLong(JsonElement value, string key) => !value.TryGetProperty(key, out var p) || p.ValueKind == JsonValueKind.Null ? null : p.GetInt64();
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static InvoiceNotificationTransportResult Review(string code) => new("NeedsReview", Code: code);
    private static void Unique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        { var names = new HashSet<string>(StringComparer.Ordinal); foreach (var p in value.EnumerateObject()) { if (!names.Add(p.Name)) throw new JsonException(); Unique(p.Value); } }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var p in value.EnumerateArray()) Unique(p);
    }
    private sealed class HttpStatusFailure(int status) : Exception { public int Status { get; } = status; }
}

/// <summary>Private artifact bytes may become available later; never submit a partial bundle.</summary>
public sealed class InvoiceAssetNotReadyException : IOException;
