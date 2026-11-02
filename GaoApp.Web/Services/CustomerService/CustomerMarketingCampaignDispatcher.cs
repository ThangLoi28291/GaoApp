using System.Text.RegularExpressions;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Infrastructure.Services.CustomerMarketing;
using GaoApp.Web.Services.Assistant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Services.CustomerService;

public sealed class CustomerMarketingCampaignOptions
{
    public const string Section = "CustomerMarketingCampaigns";
    public bool Enabled { get; set; }
    public string PilotPhone { get; set; } = "0398946174";
    public int PollSeconds { get; set; } = 10;
    public int DispatchIntervalSeconds { get; set; } = 15;
    public int LeaseSeconds { get; set; } = 90;
    public int RetrySeconds { get; set; } = 30;
    public bool Valid() => PilotPhone == "0398946174" && PollSeconds is >= 5 and <= 60 && DispatchIntervalSeconds is >= 5 and <= 300 && LeaseSeconds is >= 30 and <= 300 && RetrySeconds is >= 5 and <= 300;
}

/// <summary>Durable one-recipient campaign outbox; consent and binding are rechecked immediately before submit.</summary>
public sealed class CustomerMarketingCampaignDispatcher(AppDbContext db, TenantContext tenant, CustomerChannelRegistry registry,
    CustomerMarketingCampaignTransport transport, CustomerMarketingCampaignDispatchThrottle dispatchThrottle,
    CustomerMarketingConsentWriter consentWriter, IOptionsMonitor<CustomerMarketingCampaignOptions> options, TimeProvider clock)
{
    private static readonly string[] Terminal = ["Sent", "Skipped", "Failed", "NeedsReview"];
    private sealed record RemoteOptOutDecision(bool SourceAvailable, bool ConsentGranted, long? Sequence);

    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var settings = options.CurrentValue;
        int campaignId;
        int storeId;
        // Only discover identifiers across stores. Release the read-only grant
        // before loading a campaign or performing any store-specific work.
        using (tenant.BeginSystemAccess(SystemDataAccessPurpose.CustomerMarketingCampaignDiscovery))
        {
            var candidate = await db.CustomerMarketingCampaigns.IgnoreQueryFilters().AsNoTracking()
                .Where(x => !x.IsDeleted && x.ScheduledAtUtc <= now &&
                    (x.Status == "Dispatching" || settings.Enabled && x.Status == "Scheduled"))
                .OrderBy(x => x.ScheduledAtUtc).ThenBy(x => x.Id)
                .Select(x => new { x.Id, x.StoreId }).FirstOrDefaultAsync(ct);
            if (candidate is null) return false;
            campaignId = candidate.Id;
            storeId = candidate.StoreId;
        }

        tenant.SetStore(storeId, "customer-marketing-campaign");
        var campaign = await db.CustomerMarketingCampaigns.SingleOrDefaultAsync(x => x.Id == campaignId && x.StoreId == storeId &&
            (x.Status == "Scheduled" || x.Status == "Dispatching"), ct);
        if (campaign is null) return true;
        if (!options.CurrentValue.Enabled && campaign.Status == "Scheduled") return false;
        if (campaign.Status == "Scheduled") { campaign.Status = "Dispatching"; campaign.UpdatedAtUtc = now; await db.SaveChangesAsync(ct); }

        // P6 is a pilot only: enabling it cannot broaden delivery beyond the one pre-authorized test number.
        if (!settings.Valid())
        {
            campaign.Status = "NeedsReview"; campaign.Detail = "campaign_settings_invalid"; campaign.UpdatedAtUtc = now; await db.SaveChangesAsync(ct); return true;
        }
        if (await db.CustomerMarketingCampaignRecipients.IgnoreQueryFilters().CountAsync(x => x.StoreId == storeId && x.CampaignId == campaign.Id && !x.IsDeleted, ct) != 1 ||
            await db.CustomerMarketingCampaignRecipients.IgnoreQueryFilters().AnyAsync(x => x.StoreId == storeId && x.CampaignId == campaign.Id && !x.IsDeleted && x.Phone != settings.PilotPhone, ct))
        {
            campaign.Status = "NeedsReview"; campaign.Detail = "pilot_recipient_mismatch"; campaign.UpdatedAtUtc = now; await db.SaveChangesAsync(ct); return true;
        }

        var item = await ClaimAsync(campaign.Id, campaign.StoreId, now, settings.LeaseSeconds, options.CurrentValue.Enabled, ct);
        if (item is null) { await CompleteIfSettledAsync(campaign, now, ct); return false; }

        CustomerMarketingCampaignTransportResult result;
        var authorized = await IsAuthorizedAsync(campaign, item, ct);
        if (item.ZaloJobId is not null)
        {
            // Accepted jobs are only polled or cancelled; a retry never creates a new Zalo job.
            var remoteStop = authorized ? await ScanAndRecordRemoteStopAsync(campaign, item, ct) : new RemoteOptOutDecision(true, false, null);
            var mustCancel = !authorized || !remoteStop.SourceAvailable || !remoteStop.ConsentGranted || remoteStop.Sequence is not null;
            result = mustCancel ? await transport.CancelAndPollAsync(campaign, item, ct) :
                clock.GetUtcNow().UtcDateTime - item.CreatedAtUtc > TimeSpan.FromHours(24)
                    ? new("NeedsReview", item.ZaloJobId, "accepted_job_poll_window_expired")
                    : await transport.PollAsync(campaign, item, ct);
            if (mustCancel && result.State == "Accepted") result = new("NeedsReview", item.ZaloJobId, "consent_or_stop_cancel_unconfirmed");
        }
        else if (!options.CurrentValue.Enabled)
        {
            await SettleAsync(campaign, item, "NeedsReview", "campaign_sending_disabled_after_claim", null, now, ct);
            return true;
        }
        else if (!authorized)
        {
            await SettleAsync(campaign, item, "Skipped", "consent_or_binding_changed", null, now, ct);
            return true;
        }
        else
        {
            var dispatchAt = await dispatchThrottle.ReserveAsync(campaign.SenderProfileId, clock.GetUtcNow().UtcDateTime,
                TimeSpan.FromSeconds(settings.DispatchIntervalSeconds), ct);
            if (dispatchAt > clock.GetUtcNow().UtcDateTime)
            {
                await SettleAsync(campaign, item, "Pending", "profile_dispatch_throttled", null, dispatchAt, ct);
                return true;
            }
            string? finalAuthorizationCode = null;
            result = await transport.SubmitAsync(campaign, item, async token =>
            {
                if (!options.CurrentValue.Enabled) { finalAuthorizationCode = "campaign_sending_disabled_after_claim"; return false; }
                if (!await IsAuthorizedAsync(campaign, item, token)) { finalAuthorizationCode = "consent_or_binding_changed"; return false; }
                var check = await ScanAndRecordRemoteStopAsync(campaign, item, token);
                if (!check.SourceAvailable) { finalAuthorizationCode = "remote_opt_out_check_unavailable"; return false; }
                if (!check.ConsentGranted || check.Sequence is not null) { finalAuthorizationCode = "customer_opted_out"; return false; }
                return true;
            }, ct);
            if (result.State == "Accepted")
            {
                var remoteStop = await ScanAndRecordRemoteStopAsync(campaign, item, ct);
                if (!remoteStop.SourceAvailable || !remoteStop.ConsentGranted || remoteStop.Sequence is not null)
                {
                    item.ZaloJobId = result.JobId ?? item.ZaloJobId;
                    var cancelled = await transport.CancelAndPollAsync(campaign, item, ct);
                    result = cancelled.State == "Accepted" ? new("NeedsReview", result.JobId, "consent_or_stop_received_during_submit_cancel_unconfirmed") : cancelled;
                }
            }
            if (result.State == "AuthorizationChanged") result = finalAuthorizationCode is "remote_opt_out_check_unavailable" or "campaign_sending_disabled_after_claim"
                ? new("NeedsReview", Code: finalAuthorizationCode) : new("Skipped", Code: finalAuthorizationCode ?? result.Code);
        }

        var next = clock.GetUtcNow().UtcDateTime.AddSeconds(settings.RetrySeconds);
        switch (result.State)
        {
            case "Accepted": await SettleAsync(campaign, item, "Accepted", result.Code, result.JobId ?? item.ZaloJobId, next, ct); break;
            case "Retry" when item.Attempts < 8 && item.ZaloJobId is null: await SettleAsync(campaign, item, "Pending", result.Code ?? "submit_retry", null, next, ct); break;
            case "Sent": await SettleAsync(campaign, item, "Sent", null, result.JobId ?? item.ZaloJobId, now, ct); break;
            case "Skipped": await SettleAsync(campaign, item, "Skipped", result.Code ?? "delivery_skipped", result.JobId ?? item.ZaloJobId, now, ct); break;
            case "Failed": await SettleAsync(campaign, item, "Failed", result.Code ?? "delivery_failed", result.JobId ?? item.ZaloJobId, now, ct); break;
            default: await SettleAsync(campaign, item, "NeedsReview", result.Code ?? "campaign_outcome_unconfirmed", result.JobId ?? item.ZaloJobId, now, ct); break;
        }
        await CompleteIfSettledAsync(campaign, now, ct);
        return true;
    }

    private async Task<RemoteOptOutDecision> ScanAndRecordRemoteStopAsync(CustomerMarketingCampaign campaign, CustomerMarketingCampaignRecipient item, CancellationToken ct)
    {
        var consent = await db.CustomerMarketingConsents.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == item.StoreId &&
            x.CustomerId == item.CustomerId && x.Channel == "Zalo" && !x.IsDeleted, ct);
        if (consent?.IsGranted != true) return new(true, false, null);
        var scan = await transport.FindOptOutAsync(campaign, item, consent.ChangedAtUtc, ct);
        if (!scan.Success) return new(false, true, null);
        if (scan.Sequence is not { } sequence) return new(true, true, null);
        var evidence = $"zalo:{item.ConnectionId}:{item.ConversationId:N}:{sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        await consentWriter.SetAsync(item.StoreId, item.CustomerId, false, "ZaloKeyword", evidence, null, ct);
        return new(true, false, sequence);
    }

    private async Task<CustomerMarketingCampaignRecipient?> ClaimAsync(int campaignId, int storeId, DateTime now, int leaseSeconds, bool canStart, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var stale = now.AddSeconds(-leaseSeconds);
        var item = await db.CustomerMarketingCampaignRecipients.FromSqlInterpolated($"""
            SELECT TOP(1) * FROM dbo.CustomerMarketingCampaignRecipients WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE StoreId={storeId} AND CampaignId={campaignId} AND IsDeleted=0 AND NextAttemptAtUtc <= {now}
              AND ((Status='Accepted') OR (Status='Pending' AND {canStart}) OR (Status='Dispatching' AND UpdatedAtUtc <= {stale}))
            ORDER BY NextAttemptAtUtc, Id
            """).SingleOrDefaultAsync(ct);
        if (item is null) { await tx.CommitAsync(ct); return null; }
        item.Status = "Dispatching"; item.Attempts++; item.LastAttemptAtUtc = now; item.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return item;
    }

    private async Task<bool> IsAuthorizedAsync(CustomerMarketingCampaign campaign, CustomerMarketingCampaignRecipient item, CancellationToken ct)
    {
        if (!registry.WorkerEnabled || campaign.ScheduledAtUtc > clock.GetUtcNow().UtcDateTime ||
            campaign.ConnectionId != item.ConnectionId || campaign.ConnectionBinding != item.ConnectionBinding || campaign.SenderProfileId != item.SenderProfileId ||
            item.Phone != options.CurrentValue.PilotPhone) return false;
        var campaignLive = await db.CustomerMarketingCampaigns.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.Id == campaign.Id && x.StoreId == campaign.StoreId && !x.IsDeleted && x.Status == "Dispatching", ct);
        if (!campaignLive) return false;
        var recipientLive = await db.CustomerMarketingCampaignRecipients.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.Id == item.Id && x.CampaignId == campaign.Id && x.StoreId == campaign.StoreId && !x.IsDeleted && x.Status == "Dispatching", ct);
        if (!recipientLive) return false;
        CustomerChannelConnection current;
        try { current = registry.Get(item.ConnectionId); }
        catch (AssistantException) { return false; }
        if (!current.Enabled || current.Provider != "zalomarketing" || current.StoreId != item.StoreId || current.Binding != item.ConnectionBinding ||
            !Guid.TryParse(current.AccountId, out var profile) || profile != item.SenderProfileId) return false;

        var consent = await db.CustomerMarketingConsents.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == item.StoreId &&
            x.CustomerId == item.CustomerId && x.Channel == "Zalo" && !x.IsDeleted, ct);
        if (consent?.IsGranted != true) return false;
        var binding = await db.InvoiceRecipientBindings.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == item.BindingId &&
            x.StoreId == item.StoreId && x.CustomerId == item.CustomerId && x.RevokedAtUtc == null && x.Version == item.BindingVersion &&
            x.ConnectionId == item.ConnectionId && x.ConnectionBinding == item.ConnectionBinding && x.SenderProfileId == item.SenderProfileId &&
            x.ConversationId == item.ConversationId && x.NativePeer == item.NativePeer && x.Phone == item.Phone, ct);
        if (binding is null) return false;
        var customerPhone = await db.Customers.IgnoreQueryFilters().AsNoTracking().Where(x => x.Id == item.CustomerId && x.StoreId == item.StoreId && x.IsActive && !x.IsDeleted)
            .Select(x => x.Phone).SingleOrDefaultAsync(ct);
        return customerPhone is not null && NormalizePhone(customerPhone) == item.Phone;
    }

    private async Task SettleAsync(CustomerMarketingCampaign campaign, CustomerMarketingCampaignRecipient item, string state, string? detail, Guid? job, DateTime next, CancellationToken ct)
    {
        var fresh = await db.CustomerMarketingCampaignRecipients.SingleAsync(x => x.Id == item.Id && x.CampaignId == campaign.Id && x.StoreId == campaign.StoreId, ct);
        fresh.Status = state; fresh.Detail = Safe(detail); fresh.ZaloJobId = job ?? fresh.ZaloJobId;
        fresh.NextAttemptAtUtc = next; fresh.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        fresh.CompletedAtUtc = Terminal.Contains(state, StringComparer.Ordinal) ? clock.GetUtcNow().UtcDateTime : null;
        await db.SaveChangesAsync(ct);
    }

    private async Task CompleteIfSettledAsync(CustomerMarketingCampaign campaign, DateTime now, CancellationToken ct)
    {
        var rows = db.CustomerMarketingCampaignRecipients.IgnoreQueryFilters().Where(x => !x.IsDeleted && x.CampaignId == campaign.Id && x.StoreId == campaign.StoreId);
        if (!await rows.AnyAsync(ct) || await rows.AnyAsync(x => x.Status == "Pending" || x.Status == "Dispatching" || x.Status == "Accepted", ct)) return;
        var hasReview = await rows.AnyAsync(x => x.Status == "NeedsReview", ct);
        campaign.Status = hasReview ? "NeedsReview" : "Completed";
        campaign.Detail = hasReview ? "one_or_more_recipients_need_review" : null;
        campaign.CompletedAtUtc = now; campaign.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);
    }

    private static string NormalizePhone(string value) => new(value.Where(char.IsAsciiDigit).ToArray());
    private static string? Safe(string? value) => value is null ? null : new(value.Where(c => !char.IsControl(c)).Take(250).ToArray());
}

public sealed class CustomerMarketingCampaignWorker(IServiceScopeFactory scopes, IOptionsMonitor<CustomerMarketingCampaignOptions> options, ILogger<CustomerMarketingCampaignWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var processed = await scope.ServiceProvider.GetRequiredService<CustomerMarketingCampaignDispatcher>().ProcessNextAsync(stop);
                if (processed)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.CurrentValue.PollSeconds, 5, 60)), stop);
                    continue;
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogWarning("Marketing campaign processing unavailable: {Type}", e.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.CurrentValue.PollSeconds, 5, 60)), stop);
        }
    }
}
