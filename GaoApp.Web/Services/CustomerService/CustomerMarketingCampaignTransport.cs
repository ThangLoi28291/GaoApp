using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Web.Services.Assistant;

namespace GaoApp.Web.Services.CustomerService;

public sealed record CustomerMarketingCampaignTransportResult(string State, Guid? JobId = null, string? Code = null);
public sealed record CustomerMarketingCampaignOptOutScan(bool Success, long? Sequence = null);

/// <summary>One-recipient Direct Jobs API transport for P6 campaigns.</summary>
public sealed class CustomerMarketingCampaignTransport(HttpClient http, CustomerChannelRegistry registry)
{
    public async Task<CustomerMarketingCampaignOptOutScan> FindOptOutAsync(CustomerMarketingCampaign campaign, CustomerMarketingCampaignRecipient recipient,
        DateTime consentChangedAtUtc, CancellationToken ct)
    {
        try
        {
            var connection = Connection(campaign, recipient, sending: false);
            long? before = null;
            for (var page = 0; page < 10; page++)
            {
                var path = $"api/v1/inbox/conversations/{recipient.ConversationId:D}" + (before is null ? "" : "?before=" + before.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(registry.ZaloOrigin(connection), path));
                request.Headers.Authorization = new("Bearer", registry.Secret(connection.AccessTokenFile));
                var (_, body) = await JsonAsync(request, campaign.SenderProfileId, ct);
                if (body.GetProperty("id").GetGuid() != recipient.ConversationId || body.GetProperty("kind").GetString() != "Direct") throw new JsonException();
                var messages = body.GetProperty("messages");
                if (messages.ValueKind != JsonValueKind.Array || messages.GetArrayLength() > 50) throw new JsonException();
                long oldest = long.MaxValue;
                foreach (var message in messages.EnumerateArray())
                {
                    var sequence = message.GetProperty("sequence").GetInt64();
                    var observed = message.GetProperty("observedAtUtc").GetDateTimeOffset().UtcDateTime;
                    if (sequence <= 0) throw new JsonException();
                    oldest = Math.Min(oldest, sequence);
                    if (observed >= consentChangedAtUtc && !message.GetProperty("isOutgoing").GetBoolean() &&
                        CustomerMarketingOptOut.IsOptOut(message.GetProperty("text").GetString() ?? ""))
                        return new(true, sequence);
                }
                var more = body.GetProperty("hasOlder").GetBoolean();
                if (!more) return new(true);
                if (oldest == long.MaxValue || before is not null && oldest >= before.Value) throw new JsonException();
                if (messages.EnumerateArray().Any(m => m.GetProperty("observedAtUtc").GetDateTimeOffset().UtcDateTime < consentChangedAtUtc)) return new(true);
                before = oldest;
            }
            return new(false);
        }
        catch (Exception e) when (e is HttpFailure or HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException or AssistantException or UnauthorizedAccessException)
        { return new(false); }
    }

    public async Task<CustomerMarketingCampaignTransportResult> SubmitAsync(CustomerMarketingCampaign campaign, CustomerMarketingCampaignRecipient recipient,
        Func<CancellationToken, Task<bool>> finalRevalidate, CancellationToken ct)
    {
        try
        {
            var connection = Connection(campaign, recipient, sending: true);
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(campaign.AssetKind switch { "Image" => "ImageThenText", "File" => "FileThenText", _ => "Text" }), "DeliveryMode");
            form.Add(new StringContent(campaign.MessageText), "MessageText");
            form.Add(new StringContent(recipient.Phone), "Recipients");
            form.Add(new StringContent("0"), "MinimumDelaySeconds");
            form.Add(new StringContent("0"), "MaximumDelaySeconds");
            if (campaign.AssetBytes is not null)
            {
                var asset = new CustomerOutgoingAsset(new(campaign.AssetKind ?? "", campaign.AssetFileName ?? "", campaign.AssetContentType ?? "", campaign.AssetLength ?? 0), campaign.AssetSha256 ?? "", campaign.AssetBytes);
                CustomerOutgoingMedia.Validate(asset);
                var content = new ByteArrayContent(asset.Bytes);
                content.Headers.ContentType = new MediaTypeHeaderValue(asset.Attachment.ContentType);
                form.Add(content, "Asset", asset.Attachment.FileName);
            }
            else if (campaign.AssetKind is not null || campaign.AssetFileName is not null || campaign.AssetContentType is not null || campaign.AssetLength is not null || campaign.AssetSha256 is not null)
                throw new InvalidDataException();
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(registry.ZaloOrigin(connection), "api/v1/jobs/direct")) { Content = form };
            request.Headers.Authorization = new("Bearer", registry.Secret(connection.AccessTokenFile));
            request.Headers.Add("Idempotency-Key", IdempotencyKey(campaign, recipient));
            if (!await finalRevalidate(ct)) return new("AuthorizationChanged", Code: "consent_or_binding_changed");
            var current = Connection(campaign, recipient, sending: true);
            if (current.Binding != connection.Binding || current.AccessTokenFile != connection.AccessTokenFile) return new("NeedsReview", Code: "connection_changed_before_submit");
            var (status, body) = await JsonAsync(request, campaign.SenderProfileId, ct);
            var job = body.GetProperty("jobId").GetGuid();
            if (status != HttpStatusCode.Accepted || job == Guid.Empty || body.GetProperty("status").GetString() is not ("Pending" or "Running" or "Completed" or "Failed" or "Stopped" or "Cancelled"))
                throw new JsonException();
            return new("Accepted", job);
        }
        catch (HttpFailure e) { return e.Status is 408 or 429 or >= 500 ? new("Retry", Code: "zalo_http_" + e.Status) : new("NeedsReview", Code: "zalo_http_" + e.Status); }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException) { return new("Retry", Code: "submit_outcome_unconfirmed"); }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException or AssistantException or UnauthorizedAccessException)
        { return new("NeedsReview", Code: "campaign_submit_contract_invalid"); }
    }

    public async Task<CustomerMarketingCampaignTransportResult> PollAsync(CustomerMarketingCampaign campaign, CustomerMarketingCampaignRecipient recipient, CancellationToken ct)
    {
        if (recipient.ZaloJobId is not { } job || job == Guid.Empty) return new("NeedsReview", Code: "accepted_job_missing");
        try
        {
            var connection = Connection(campaign, recipient, sending: false);
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(registry.ZaloOrigin(connection), $"api/v1/jobs/{job:D}"));
            request.Headers.Authorization = new("Bearer", registry.Secret(connection.AccessTokenFile));
            var (_, body) = await JsonAsync(request, campaign.SenderProfileId, ct);
            return ParseStatus(job, campaign.SenderProfileId, body);
        }
        catch (HttpFailure e) { return e.Status is 408 or 429 or >= 500 ? new("Accepted", recipient.ZaloJobId, "poll_unavailable") : new("NeedsReview", recipient.ZaloJobId, "poll_http_" + e.Status); }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException) { return new("Accepted", recipient.ZaloJobId, "poll_unavailable"); }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException or AssistantException or UnauthorizedAccessException)
        { return new("NeedsReview", recipient.ZaloJobId, "campaign_poll_contract_invalid"); }
    }

    public async Task<CustomerMarketingCampaignTransportResult> CancelAndPollAsync(CustomerMarketingCampaign campaign, CustomerMarketingCampaignRecipient recipient, CancellationToken ct)
    {
        var observed = await PollAsync(campaign, recipient, ct);
        if (observed.State is "Sent" or "Failed" or "Skipped" or "NeedsReview") return observed;
        var job = recipient.ZaloJobId!.Value;
        try
        {
            var connection = Connection(campaign, recipient, sending: false);
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(registry.ZaloOrigin(connection), $"api/v1/jobs/{job:D}/cancel"));
            request.Headers.Authorization = new("Bearer", registry.Secret(connection.AccessTokenFile));
            var (_, body) = await JsonAsync(request, campaign.SenderProfileId, ct, requireProfileHeader: false);
            if (body.GetProperty("jobId").GetGuid() != job || body.GetProperty("status").GetString() != "Cancelled") throw new JsonException();
            var settled = await PollAsync(campaign, recipient, ct);
            return settled.State == "Accepted" ? new("Accepted", job, "cancel_not_terminal") : settled;
        }
        catch (HttpFailure e) { return e.Status is 408 or 429 or >= 500 or 409 ? new("Accepted", job, "cancel_unconfirmed") : new("NeedsReview", job, "cancel_http_" + e.Status); }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException) { return new("Accepted", job, "cancel_unconfirmed"); }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException or AssistantException or UnauthorizedAccessException)
        { return new("NeedsReview", job, "cancel_contract_invalid"); }
    }

    private CustomerChannelConnection Connection(CustomerMarketingCampaign campaign, CustomerMarketingCampaignRecipient recipient, bool sending)
    {
        if (campaign.Id <= 0 || campaign.CampaignKey == Guid.Empty || recipient.Id <= 0 || campaign.StoreId != recipient.StoreId ||
            campaign.ConnectionId != recipient.ConnectionId || campaign.ConnectionBinding != recipient.ConnectionBinding || campaign.SenderProfileId != recipient.SenderProfileId ||
            recipient.CustomerId <= 0 || recipient.BindingId == Guid.Empty || recipient.BindingVersion <= 0 || recipient.ConversationId == Guid.Empty ||
            recipient.Phone.Length is < 10 or > 20 || !recipient.Phone.All(char.IsAsciiDigit) || recipient.NativePeer.Length is < 1 or > 128)
            throw new InvalidDataException();
        var connection = registry.Get(recipient.ConnectionId);
        if (connection.Provider != "zalomarketing" || connection.StoreId != campaign.StoreId || connection.Binding != recipient.ConnectionBinding ||
            !Guid.TryParse(connection.AccountId, out var profile) || profile != campaign.SenderProfileId ||
            sending && !connection.Enabled) throw new InvalidDataException();
        return connection;
    }

    private static string IdempotencyKey(CustomerMarketingCampaign campaign, CustomerMarketingCampaignRecipient recipient)
        => "gao-campaign-" + campaign.CampaignKey.ToString("N") + "-" + recipient.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private async Task<(HttpStatusCode Status, JsonElement Body)> JsonAsync(HttpRequestMessage request, Guid profile, CancellationToken ct, bool requireProfileHeader = true)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(45));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token);
        if (!response.IsSuccessStatusCode) throw new HttpFailure((int)response.StatusCode);
        if (requireProfileHeader && (!response.Headers.TryGetValues("X-ZaloMarketing-Sender-Profile-Id", out var values) || values.Count() != 1 ||
            !Guid.TryParseExact(values.Single(), "D", out var actual) || actual != profile)) throw new JsonException();
        await response.Content.LoadIntoBufferAsync(1024 * 1024).WaitAsync(budget.Token);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(budget.Token), cancellationToken: budget.Token);
        Unique(document.RootElement);
        return (response.StatusCode, document.RootElement.Clone());
    }

    private static CustomerMarketingCampaignTransportResult ParseStatus(Guid job, Guid expectedProfile, JsonElement body)
    {
        if (body.GetProperty("jobId").GetGuid() != job || !body.TryGetProperty("senderProfileId", out var profile) ||
            profile.ValueKind != JsonValueKind.String || profile.GetGuid() != expectedProfile)
            throw new JsonException();
        var summary = body.GetProperty("summary");
        if (summary.GetProperty("total").GetInt32() != 1) throw new JsonException();
        if (summary.GetProperty("sent").GetInt32() == 1) return new("Sent", job);
        if (summary.GetProperty("needsReview").GetInt32() > 0 || summary.GetProperty("partiallySent").GetInt32() > 0) return new("NeedsReview", job, "zalo_needs_review");
        if (summary.GetProperty("skipped").GetInt32() == 1) return new("Skipped", job, "zalo_skipped");
        if (summary.GetProperty("failed").GetInt32() == 1 || summary.GetProperty("notFound").GetInt32() == 1) return new("Failed", job, "zalo_delivery_failed");
        return body.GetProperty("status").GetString() is "Cancelled" or "Stopped" or "Failed"
            ? new("Skipped", job, "zalo_job_stopped") : new("Accepted", job);
    }

    private static void Unique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        { var names = new HashSet<string>(StringComparer.Ordinal); foreach (var p in value.EnumerateObject()) { if (!names.Add(p.Name)) throw new JsonException(); Unique(p.Value); } }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var p in value.EnumerateArray()) Unique(p);
    }

    private sealed class HttpFailure(int status) : Exception { public int Status { get; } = status; }
}
