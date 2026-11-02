using System.Net;
using System.Net.Http.Json;
using GaoApp.Domain.Entities;
using GaoApp.Web.Services.CustomerService;
using Xunit;

namespace GaoApp.Tests.Security;

public sealed partial class CustomerChannelTransportTests
{
    private static CustomerMarketingCampaign Campaign(CustomerChannelConnection c) => new()
    {
        Id = 21, CampaignKey = Guid.NewGuid(), StoreId = c.StoreId, Name = "Synthetic P6 campaign", MessageText = "TEST synthetic campaign body",
        ConnectionId = c.Id, ConnectionBinding = c.Binding, SenderProfileId = Guid.Parse(c.AccountId), SnapshotSha256 = new string('a', 64),
        RecipientCount = 1, ScheduledAtUtc = DateTime.UtcNow.AddMinutes(10), Status = "Dispatching", CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = 1
    };

    private static CustomerMarketingCampaignRecipient CampaignRecipient(CustomerChannelConnection c) => new()
    {
        Id = 34, CampaignId = 21, StoreId = c.StoreId, CustomerId = 56, BindingId = Guid.NewGuid(), BindingVersion = 1,
        ConnectionId = c.Id, ConnectionBinding = c.Binding, SenderProfileId = Guid.Parse(c.AccountId), ConversationId = Guid.NewGuid(),
        NativePeer = "sdk-v1:123456", Phone = "0398946174", Status = "Dispatching", NextAttemptAtUtc = DateTime.UtcNow
    };

    [Fact]
    public async Task Campaign_direct_job_uses_single_test_recipient_idempotency_and_profile_attestation()
    {
        var c = Zalo(); var campaign = Campaign(c); var recipient = CampaignRecipient(c); var job = Guid.NewGuid(); string? body = null; string? key = null;
        using var http = new HttpClient(new Handler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://zalo.example.test/api/v1/jobs/direct", request.RequestUri!.ToString());
            Assert.Equal("Bearer SYNTHETIC_BEARER_TOKEN_ONLY", request.Headers.Authorization!.ToString());
            key = request.Headers.GetValues("Idempotency-Key").Single();
            body = await request.Content!.ReadAsStringAsync();
            var response = new HttpResponseMessage(HttpStatusCode.Accepted) { Content = JsonContent.Create(new { jobId = job, status = "Pending" }) };
            response.Headers.Add("X-ZaloMarketing-Sender-Profile-Id", c.AccountId);
            return response;
        }));
        var wire = new CustomerMarketingCampaignTransport(http, Registry(c));
        var result = await wire.SubmitAsync(campaign, recipient, _ => Task.FromResult(true), default);
        Assert.Equal("Accepted", result.State); Assert.Equal(job, result.JobId);
        Assert.Equal("gao-campaign-" + campaign.CampaignKey.ToString("N") + "-34", key);
        Assert.Contains("0398946174", body); Assert.Contains("TEST synthetic campaign body", body);
    }

    [Fact]
    public async Task Campaign_direct_job_sends_validated_file_as_FileThenText()
    {
        var c = Zalo(); var campaign = Campaign(c); var recipient = CampaignRecipient(c); var job = Guid.NewGuid(); string? body = null;
        campaign.AssetKind = "File"; campaign.AssetFileName = "invoice.pdf"; campaign.AssetContentType = "application/pdf";
        campaign.AssetBytes = "%PDF-1.7 synthetic"u8.ToArray(); campaign.AssetLength = campaign.AssetBytes.Length;
        campaign.AssetSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(campaign.AssetBytes));
        using var http = new HttpClient(new Handler(async request =>
        {
            body = await request.Content!.ReadAsStringAsync();
            var response = new HttpResponseMessage(HttpStatusCode.Accepted) { Content = JsonContent.Create(new { jobId = job, status = "Pending" }) };
            response.Headers.Add("X-ZaloMarketing-Sender-Profile-Id", c.AccountId); return response;
        }));
        var result = await new CustomerMarketingCampaignTransport(http, Registry(c)).SubmitAsync(campaign, recipient, _ => Task.FromResult(true), default);
        Assert.Equal("Accepted", result.State);
        Assert.Contains("FileThenText", body);
        Assert.Contains("invoice.pdf", body);
        Assert.Contains("%PDF-1.7 synthetic", body);
    }

    [Fact]
    public async Task Campaign_inbox_scan_finds_exact_stop_keyword_and_sequence()
    {
        var c = Zalo(); var campaign = Campaign(c); var recipient = CampaignRecipient(c);
        var at = DateTimeOffset.UtcNow;
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal($"https://zalo.example.test/api/v1/inbox/conversations/{recipient.ConversationId:D}", request.RequestUri!.ToString());
            var response = Json(new { id = recipient.ConversationId, kind = "Direct", hasOlder = false,
                messages = new[] { new { sequence = 51L, text = "STOP", observedAtUtc = at, isOutgoing = false } } });
            response.Headers.Add("X-ZaloMarketing-Sender-Profile-Id", c.AccountId); return Task.FromResult(response);
        }));
        var result = await new CustomerMarketingCampaignTransport(http, Registry(c)).FindOptOutAsync(campaign, recipient, at.AddMinutes(-1).UtcDateTime, default);
        Assert.True(result.Success); Assert.Equal(51L, result.Sequence);
    }

    [Fact]
    public async Task Campaign_inbox_scan_fails_closed_when_source_unavailable()
    {
        var c = Zalo();
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
        var result = await new CustomerMarketingCampaignTransport(http, Registry(c)).FindOptOutAsync(Campaign(c), CampaignRecipient(c), DateTime.UtcNow.AddDays(-1), default);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task Campaign_does_not_submit_when_final_consent_check_fails()
    {
        var c = Zalo(); var calls = 0;
        using var http = new HttpClient(new Handler(_ => { calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)); }));
        var result = await new CustomerMarketingCampaignTransport(http, Registry(c)).SubmitAsync(Campaign(c), CampaignRecipient(c), _ => Task.FromResult(false), default);
        Assert.Equal("AuthorizationChanged", result.State); Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(1, 0, 0, 0, 0, "Sent")]
    [InlineData(0, 1, 0, 0, 0, "Failed")]
    [InlineData(0, 0, 1, 0, 0, "Skipped")]
    [InlineData(0, 0, 0, 1, 0, "NeedsReview")]
    public async Task Campaign_poll_maps_only_attested_single_recipient_outcomes(int sent, int failed, int skipped, int needsReview, int partial, string expected)
    {
        var c = Zalo(); var job = Guid.NewGuid();
        using var http = new HttpClient(new Handler(_ =>
        {
            var response = Json(new { jobId = job, senderProfileId = Guid.Parse(c.AccountId), status = "Completed",
                summary = new { total = 1, sent, failed, skipped, needsReview, partiallySent = partial, notFound = 0 } });
            response.Headers.Add("X-ZaloMarketing-Sender-Profile-Id", c.AccountId); return Task.FromResult(response);
        }));
        var item = CampaignRecipient(c); item.ZaloJobId = job;
        var result = await new CustomerMarketingCampaignTransport(http, Registry(c)).PollAsync(Campaign(c), item, default);
        Assert.Equal(expected, result.State);
    }

    [Fact]
    public async Task Campaign_poll_rejects_wrong_profile_without_reporting_delivery()
    {
        var c = Zalo(); var job = Guid.NewGuid();
        using var http = new HttpClient(new Handler(_ =>
        {
            var response = Json(new { jobId = job, senderProfileId = Guid.NewGuid(), status = "Completed",
                summary = new { total = 1, sent = 1, failed = 0, skipped = 0, needsReview = 0, partiallySent = 0, notFound = 0 } });
            response.Headers.Add("X-ZaloMarketing-Sender-Profile-Id", c.AccountId); return Task.FromResult(response);
        }));
        var item = CampaignRecipient(c); item.ZaloJobId = job;
        var result = await new CustomerMarketingCampaignTransport(http, Registry(c)).PollAsync(Campaign(c), item, default);
        Assert.Equal("NeedsReview", result.State);
    }
}
