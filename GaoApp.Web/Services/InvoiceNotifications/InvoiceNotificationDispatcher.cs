using GaoApp.Application.InvoiceNotifications;
using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Services.CustomerService;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Services.InvoiceNotifications;

public sealed class InvoiceNotificationDispatcher(IInvoiceNotificationOutbox outbox, IInvoiceNotificationBundleBuilder builder,
    InvoiceNotificationTransport transport, CustomerChannelRegistry registry, IOptionsMonitor<InvoiceNotificationOptions> options)
{
    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        // Core Claim forbids new sends while disabled but still leases known accepted jobs for cancellation/settlement.
        ct.ThrowIfCancellationRequested();
        var item = await outbox.ClaimAsync(ct);
        if (item is null) return false;
        // Bound the whole build/read/control/poll chain below the core lease, rather than 45 seconds per HTTP call.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.CurrentValue.LeaseSeconds - 5, 10, 90)));
        ct = budget.Token;
        InvoiceNotificationTransportResult result;
        try
        {
            if (item.ZaloJobId is not null)
            {
                // A known accepted job is immutable. Revocation/source changes stop its unsent parts, never create a replacement.
                result = item.Prepared is null ? new("NeedsReview", Code: "accepted_bundle_missing") :
                    await outbox.IsAuthorizedAsync(item, CurrentBinding(item), ct) ? await transport.PollAsync(item, item.Prepared, ct) : await transport.CancelAndPollAsync(item, item.Prepared, ct);
            }
            else
            {
                if (!await outbox.IsAuthorizedAsync(item, CurrentBinding(item), ct))
                    (item, result) = await AuthorizationChangedAsync(item, ct);
                else
                {
                    if (item.Prepared is null)
                    {
                        var bundle = builder is IInvoiceWorkItemBundleBuilder workAware ? await workAware.BuildAsync(item, ct) : await builder.BuildAsync(item.Snapshot, ct);
                        item = await outbox.SavePreparedAsync(item, bundle, ct);
                    }
                    result = await transport.SendAsync(item, item.Prepared!, token => outbox.IsAuthorizedAsync(item, CurrentBinding(item), token), ct);
                    if (result.State == "AuthorizationChanged") (item, result) = await AuthorizationChangedAsync(item, ct);
                }
            }
        }
        catch (InvoiceOfficialDocumentLinkNotConfiguredException) { result = new("NeedsReview", Code: "official_link_not_configured"); }
        catch (IOException) { result = new("Retry", Code: "artifact_not_ready"); }
        catch (OperationCanceledException) { result = new(item.ZaloJobId is null ? "Retry" : "Accepted", item.ZaloJobId, "outcome_unconfirmed"); }
        catch (Exception e) when (e is InvalidOperationException or InvalidDataException or ArgumentException or KeyNotFoundException or AssistantException or UnauthorizedAccessException)
        { result = new("NeedsReview", Code: "invoice_preparation_or_connection_invalid"); }

        // Once an HTTP attempt may have occurred, shutdown cannot suppress its durable settlement.
        // If SQL is unavailable, lease recovery still reuses the same request/key and prepared content.
        switch (result.State)
        {
            case "Handled": break;
            case "Accepted" when item.ZaloJobId is null && result.JobId is { } job:
                await outbox.RecordAcceptedAsync(item, job, CancellationToken.None); break;
            case "Accepted":
            case "Retry":
                await outbox.RecordRetryAsync(item, result.Code ?? "result_pending", TimeSpan.FromSeconds(item.ZaloJobId is null ? 30 : 5), CancellationToken.None); break;
            case "Sent": case "PartiallySent": case "Failed": case "Blocked": case "Cancelled":
                await outbox.RecordPartsOutcomeAsync(item, result.State, result.PartsOutcomeJson ?? "{}", CancellationToken.None); break;
            default:
                if (result.PartsOutcomeJson is not null) await outbox.RecordPartsOutcomeAsync(item, "NeedsReview", result.PartsOutcomeJson, CancellationToken.None);
                else await outbox.RecordReviewAsync(item, result.Code ?? "outcome_unconfirmed", CancellationToken.None);
                break;
        }
        return true;
    }

    private async Task<(InvoiceNotificationWorkItem, InvoiceNotificationTransportResult)> AuthorizationChangedAsync(InvoiceNotificationWorkItem item, CancellationToken ct)
    {
        if (item.Prepared is not null && item.Attempts > 1)
        {
            // The acknowledgement of an earlier POST may have been lost. Discover only its deterministic request ID.
            var probe = item with { ZaloJobId = item.RequestId };
            var observed = await transport.PollAsync(probe, item.Prepared, ct);
            if (observed.PartsOutcomeJson is not null)
            {
                item = await outbox.AttachDiscoveredAcceptedAsync(item, item.RequestId, ct);
                return (item, await transport.CancelAndPollAsync(item, item.Prepared!, ct));
            }
            // A revoked in-flight POST can commit after an initial 404. Keep probing the same immutable ID
            // until core's first-attempt recovery window expires; a missing early read is not proof of no acceptance.
            var retryProbe = observed.State == "Accepted" || observed.Code == "poll_http_404";
            return (item, new(retryProbe ? "Retry" : "NeedsReview", Code: "prior_submit_probe_" + (observed.Code ?? "unconfirmed")));
        }
        // No prior possible POST: the ordinary core denial can now clear the lease safely.
        var stillValid = await outbox.RevalidateAsync(item, CurrentBinding(item), ct);
        return (item, new(stillValid ? "Retry" : "Handled", Code: "source_or_recipient_changed"));
    }

    private string CurrentBinding(InvoiceNotificationWorkItem item)
    {
        var c = registry.Get(item.Binding.ConnectionId);
        var binding = c.Enabled && c.Provider == "zalomarketing" && c.StoreId == item.StoreId &&
            Guid.TryParse(c.AccountId, out var profile) && profile == item.Binding.SenderProfileId ? c.Binding : "";
        return binding;
    }
}
