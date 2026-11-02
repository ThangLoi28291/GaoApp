using GaoApp.Domain.Entities;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb07"), Trait("Category", "StorefrontWeb07")]
public sealed class Web07HttpTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Real_guest_request_then_failed_full_return_or_exported_incident_recovery_closes_pending_without_cancellation_approval_permission(bool incident)
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture, prepaidQr: true); var physical = c.Physical!;
        await physical.PrepareAsync(); await physical.PickAllAsync(); await physical.DispatchAsync();
        await using var publicHost = await Web07PublicHost.StartAsync(c); await publicHost.CsrfAsync();
        using (var request = await publicHost.Http.PostAsJsonAsync("/shop/orders/cancellation", await c.CancelRequestAsync())) Assert.Equal(HttpStatusCode.OK, request.StatusCode);
        // Actual existing physical MVC/controller/service, with real role grants
        // for custody/returns only. No cancellation approval grant is seeded.
        await using var staff = await Web06AdminHost.StartAsync(physical, codes: Web06Support.PhysicalPermissions);
        using var client = staff.Client(); await Web06AdminHost.Csrf(client, c.OrderId);
        string route; StorefrontOperationRequest finalCommand;
        if (incident)
        {
            using (var report = await client.PostAsJsonAsync($"/admin/storefront/fulfillment/{c.OrderId}/incident", await physical.IncidentRequestAsync())) Assert.Equal(HttpStatusCode.OK, report.StatusCode);
            int id; await using (var db = c.Context()) id = (await db.Set<StorefrontCustodyIncident>().SingleAsync()).Id;
            finalCommand = await physical.ReconcileRequestAsync(id, StorefrontIncidentResolution.AllGoodsRecoveredAtSource); route = "reconcile-incident";
        }
        else
        {
            var detail = await physical.DetailAsync(); var outcome = await physical.RequestAsync<StorefrontDeliveryOutcomeRequest>();
            outcome.FulfillmentSourceId = detail.Children.Single().SourceId; outcome.Delivered = false; outcome.ResultOccurredAt = DateTimeOffset.UtcNow;
            outcome.EvidenceReference = "WEB07 actual failed-delivery HTTP evidence; not customer receipt";
            using (var result = await client.PostAsJsonAsync($"/admin/storefront/fulfillment/{c.OrderId}/outcome", outcome)) Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            finalCommand = await physical.ReturnRequestAsync(); route = "return";
        }
        using (var result = await client.PostAsJsonAsync($"/admin/storefront/fulfillment/{c.OrderId}/{route}", (object)finalCommand)) Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var after = await c.SnapshotAsync();
        using (var replay = await client.PostAsJsonAsync($"/admin/storefront/fulfillment/{c.OrderId}/{route}", (object)finalCommand)) Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(after, await c.SnapshotAsync());
        using (var access = await publicHost.Http.PostAsJsonAsync("/shop/orders/cancellation/access", new { token = c.Token }))
        {
            Assert.Equal(HttpStatusCode.OK, access.StatusCode); var state = (await access.Content.ReadFromJsonAsync<StorefrontGuestCancellationView>())!;
            Assert.Equal(StorefrontOrderPhase.Cancelled, state.Phase); Assert.False(state.CancellationPending);
            var refund = Assert.Single(state.Refunds); Assert.Equal(230_000m, refund.Amount); Assert.Equal(0m, refund.ExecutedAmount); Assert.Equal(StorefrontRefundState.Requested, refund.State);
        }
        await using var check = c.Context();
        var decision = Assert.Single(await check.Set<StorefrontCancellationDecision>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
        Assert.Equal(StorefrontCancellationDecisionKind.SupersededByActualReturn, decision.Kind); Assert.NotNull(decision.ActualReturnEvidenceId);
        Assert.Equal(4m, await check.DeliveryReturnedCostFragments.SumAsync(x => x.BaseQuantity)); Assert.Equal(40_000m, await check.DeliveryReturnedCostFragments.SumAsync(x => x.CostAmount));
        Assert.Equal(230_000m, await check.Set<StorefrontRefundFunding>().Where(x => x.StorefrontOrderId == c.OrderId).SumAsync(x => x.Amount));
        Assert.Empty(await check.POSShiftCashTransactions.ToListAsync()); Assert.Empty(await check.TreasuryEntries.ToListAsync()); Assert.Empty(await check.Orders.ToListAsync());
        var role = await check.UserInStores.Where(x => x.UserId == c.Seed.UserId).Select(x => x.RoleId).SingleAsync();
        Assert.False(await check.RolePermissions.AnyAsync(x => x.RoleId == role && check.Permissions.Any(p => p.Id == x.PermissionId && p.Code == StorefrontPermissions.ApproveCancellation)));
    }

    [Theory]
    [InlineData("/shop/orders/cancellation/access")] [InlineData("/shop/orders/cancellation")]
    public async Task Actual_guest_cancellation_posts_require_the_real_antiforgery_cookie_and_header(string route)
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture); await using var host = await Web07PublicHost.StartAsync(c);
        var command = await c.CancelRequestAsync(); var before = await c.SnapshotAsync();
        using var response = await host.Http.PostAsJsonAsync(route, command);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(before, await c.SnapshotAsync());
    }

    [Theory]
    [InlineData("")] [InlineData("not-a-capability")] [InlineData("code")] [InlineData("phone")] [InlineData("foreign")]
    public async Task Actual_guest_lookup_requires_its_own_capability_and_does_not_disclose_order_or_financial_details(string kind)
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture); var foreign = kind == "foreign" ? await Web07Case.CreatePhysicalAsync(fixture) : null;
        var token = kind switch { "code" => c.Placed.Code, "phone" => c.Checkout.Request().RecipientPhone, "foreign" => foreign!.Token, _ => kind };
        await using var host = await Web07PublicHost.StartAsync(c); await host.CsrfAsync(); var before = await c.SnapshotAsync();
        using var response = await host.Http.PostAsJsonAsync("/shop/orders/cancellation/access", new { token });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain(c.Placed.Code, text); Web07Support.NoCapability(text, c.Token, "invalid capability HTTP response");
        Assert.Equal(before, await c.SnapshotAsync());
    }

    [Fact]
    public async Task Closed_new_sales_still_allow_existing_guest_cancellation_and_truthful_refund_progress_without_a_public_admin_endpoint()
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture, prepaidQr: true);
        await using (var db = c.Context()) await db.StorefrontSettings.ExecuteUpdateAsync(x => x.SetProperty(s => s.IsEnabled, false));
        string log;
        await using (var host = await Web07PublicHost.StartAsync(c))
        {
            log = host.LogPath; await host.CsrfAsync();
            using (var access = await host.Http.PostAsJsonAsync("/shop/orders/cancellation/access", new { token = c.Token }))
            {
                Assert.Equal(HttpStatusCode.OK, access.StatusCode); Assert.True(access.Headers.CacheControl?.NoStore);
                var state = (await access.Content.ReadFromJsonAsync<StorefrontGuestCancellationView>())!;
                Assert.True(state.CanSelfCancel); Assert.Equal(c.Placed.Code, state.Code);
            }
            var command = await c.CancelRequestAsync();
            using (var result = await host.Http.PostAsJsonAsync("/shop/orders/cancellation", command))
            { Assert.Equal(HttpStatusCode.OK, result.StatusCode); Assert.True(result.Headers.CacheControl?.NoStore); Web07Support.NoCapability(await result.Content.ReadAsStringAsync(), c.Token, "cancellation HTTP acknowledgement"); }
            using (var access = await host.Http.PostAsJsonAsync("/shop/orders/cancellation/access", new { token = c.Token }))
            {
                Assert.Equal(HttpStatusCode.OK, access.StatusCode); var state = (await access.Content.ReadFromJsonAsync<StorefrontGuestCancellationView>())!;
                Assert.Equal(StorefrontOrderPhase.Cancelled, state.Phase); Assert.False(state.CanSelfCancel);
                var refund = Assert.Single(state.Refunds); Assert.Equal(230_000m, refund.Amount); Assert.Equal(0m, refund.ExecutedAmount);
                Assert.Equal(StorefrontRefundState.Requested, refund.State); Assert.NotEmpty(state.History);
            }
            using (var admin = await host.Http.GetAsync("/admin/storefront/refunds/" + c.OrderId)) Assert.Equal(HttpStatusCode.NotFound, admin.StatusCode);
        }
        Web07Support.NoCapability(await File.ReadAllTextAsync(log), c.Token, "owned full public-host log");
    }

    [Theory]
    [InlineData("/shop/orders/cancellation/access")] [InlineData("/shop/orders/cancellation")]
    public async Task Capability_cancellation_endpoints_are_post_only_and_never_accept_a_query_capability(string route)
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture); await using var host = await Web07PublicHost.StartAsync(c);
        using var response = await host.Http.GetAsync(route); Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        await using var db = c.Context(); Assert.Empty(await db.Set<StorefrontCancellationRequest>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
    }

    [Theory]
    [InlineData("Production", true)] [InlineData("Development", false)]
    public async Task Closed_production_and_nonpreview_customer_hosts_do_not_expose_cancellation_routes(string environment, bool preview)
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture); await using var host = await Web07PublicHost.StartAsync(c, environment, preview);
        foreach (var route in new[] { "/shop/orders/cancellation/access", "/shop/orders/cancellation" })
        { using var response = await host.Http.PostAsJsonAsync(route, new { token = c.Token }); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); }
    }

    [Theory]
    [InlineData("bad-version")] [InlineData("short-version")] [InlineData("stale-version")] [InlineData("stale-revision")] [InlineData("empty-guid")]
    public async Task Guest_command_version_and_key_validation_rejects_changes_without_releasing_holds_or_creating_a_refund(string invalid)
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture, prepaidQr: true); await using var host = await Web07PublicHost.StartAsync(c); await host.CsrfAsync();
        var command = await c.CancelRequestAsync();
        switch (invalid)
        {
            case "bad-version": command.ExpectedVersion = "!not-base64!"; break;
            case "short-version": command.ExpectedVersion = Convert.ToBase64String(new byte[7]); break;
            case "stale-version": command.ExpectedVersion = Convert.ToBase64String(new byte[8]); break;
            case "stale-revision": command.ExpectedRevision--; break;
            case "empty-guid": command.ClientRequestId = Guid.Empty; break;
        }
        var before = await c.SnapshotAsync(); using var response = await host.Http.PostAsJsonAsync("/shop/orders/cancellation", command);
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.UnprocessableEntity, HttpStatusCode.Conflict }); Assert.Equal(before, await c.SnapshotAsync());
    }

    [Fact]
    public async Task Guest_same_key_replay_returns_original_outcome_after_cancellation_but_changed_reason_is_a_conflict()
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture); await using var host = await Web07PublicHost.StartAsync(c); await host.CsrfAsync();
        var command = await c.CancelRequestAsync();
        using var first = await host.Http.PostAsJsonAsync("/shop/orders/cancellation", command); Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var original = (await first.Content.ReadFromJsonAsync<StorefrontOperationAck>())!; Assert.False(original.Replayed); var after = await c.SnapshotAsync();
        using var replay = await host.Http.PostAsJsonAsync("/shop/orders/cancellation", command);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal(original with { Replayed = true }, await replay.Content.ReadFromJsonAsync<StorefrontOperationAck>()); Assert.Equal(after, await c.SnapshotAsync());
        command.Reason += " changed"; using var changed = await host.Http.PostAsJsonAsync("/shop/orders/cancellation", command);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode); Assert.Equal(after, await c.SnapshotAsync());
    }

    [Fact]
    public async Task Guest_JSON_size_and_content_type_limits_fail_without_running_the_command()
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture); await using var host = await Web07PublicHost.StartAsync(c); await host.CsrfAsync(); var before = await c.SnapshotAsync();
        using (var large = await host.Http.PostAsync("/shop/orders/cancellation", new StringContent(JsonSerializer.Serialize(new { token = c.Token, reason = new string('x', 20_000) }), Encoding.UTF8, "application/json")))
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, large.StatusCode);
        using (var form = await host.Http.PostAsync("/shop/orders/cancellation", new FormUrlEncodedContent(new Dictionary<string, string> { ["Token"] = c.Token })))
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, form.StatusCode);
        Assert.Equal(before, await c.SnapshotAsync());
    }

    [Fact]
    public async Task Actual_cancelled_QR_is_a_closed_read_projection_with_no_private_image_or_payment_instructions()
    {
        var c = await Web07Case.CreateUnpaidQrAsync(fixture); var bank = c.Banking!;
        int intentId; string reference; string account;
        await using (var db = c.Context())
        {
            var intent = await db.Set<StorefrontPaymentIntent>().SingleAsync(); intentId = intent.Id; reference = intent.ProviderReference;
            account = (await db.StoreBankAccounts.SingleAsync(x => x.Id == c.Seed.BankAccountId)).AccountNumber;
        }
        var transport = new Web05BankTransport(account, c.Clock); await bank.ProcessAsync(intentId, transport);
        await using var host = await Web07PublicHost.StartAsync(c); await host.CsrfAsync();
        using (var before = await host.Http.PostAsJsonAsync("/shop/orders/qr/image", new { token = c.Token })) Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        using (var cancel = await host.Http.PostAsJsonAsync("/shop/orders/cancellation", await c.CancelRequestAsync())) Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        using (var state = await host.Http.PostAsJsonAsync("/shop/orders/qr", new { token = c.Token }))
        {
            Assert.Equal(HttpStatusCode.OK, state.StatusCode); using var json = JsonDocument.Parse(await state.Content.ReadAsStringAsync());
            Assert.Equal(6, json.RootElement.GetProperty("state").GetInt32());
            Assert.False(json.RootElement.GetProperty("qrImageReady").GetBoolean());
            Assert.True(string.IsNullOrEmpty(json.RootElement.GetProperty("virtualAccount").GetString()));
            Assert.True(string.IsNullOrEmpty(json.RootElement.GetProperty("content").GetString()));
        }
        using (var image = await host.Http.PostAsJsonAsync("/shop/orders/qr/image", new { token = c.Token })) Assert.Equal(HttpStatusCode.NotFound, image.StatusCode);
        transport.Pay(reference, 45_000m); await bank.ProcessAsync(intentId, transport);
        await using var check = c.Context();
        Assert.NotEqual(6, (await check.Set<StorefrontQrProcessing>().SingleAsync()).State);
        Assert.Equal(StorefrontOrderPhase.Cancelled, (await check.StorefrontOrders.SingleAsync()).Phase);
        Assert.Equal(45_000m, await check.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId).SumAsync(x => x.Amount));
        Assert.Equal(45_000m, await check.Set<StorefrontRefundFunding>().Where(x => x.StorefrontOrderId == c.OrderId).SumAsync(x => x.Amount));
        Assert.Empty(await check.Set<StorefrontRefundEvidence>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
    }

    [Theory]
    [InlineData("cancellation/approve", StorefrontPermissions.ApproveCancellation)]
    [InlineData("cancellation/reject", StorefrontPermissions.ApproveCancellation)]
    [InlineData("approve", StorefrontPermissions.ApproveRefund)]
    [InlineData("reject", StorefrontPermissions.ApproveRefund)]
    [InlineData("confirm-execution", StorefrontPermissions.ConfirmRefund)]
    [InlineData("confirm-execution", PermissionCodes.Report.Treasury.Manage)]
    public async Task Admin_actions_require_actual_role_permission_before_any_mutation(string route, string missing)
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture, prepaidQr: true);
        await using var host = await Web07AdminHost.StartAsync(c); using var client = host.Client(); await Web07AdminHost.CsrfAsync(client, c.OrderId);
        await Web07AdminHost.RevokeAsync(c, missing); var before = await c.SnapshotAsync();
        using var response = await client.PostAsJsonAsync($"/admin/storefront/refunds/{c.OrderId}/{route}", await c.CommandAsync<StorefrontOperationRequest>());
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); Assert.Equal(before, await c.SnapshotAsync());
    }

    [Theory]
    [InlineData("cancellation/approve")] [InlineData("cancellation/reject")] [InlineData("cancellation/acknowledge-delivery")]
    [InlineData("approve")] [InlineData("reject")] [InlineData("confirm-execution")]
    public async Task Every_real_admin_mutation_requires_its_own_antiforgery_token(string route)
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture); await using var host = await Web07AdminHost.StartAsync(c); using var client = host.Client();
        var before = await c.SnapshotAsync(); using var response = await client.PostAsJsonAsync($"/admin/storefront/refunds/{c.OrderId}/{route}", new { });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(before, await c.SnapshotAsync());
    }

    [Fact]
    public async Task Admin_state_and_payout_sources_require_real_login_tenant_and_both_execution_grants()
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture); var foreign = await Web07Case.CreatePhysicalAsync(fixture);
        await using var host = await Web07AdminHost.StartAsync(c);
        using (var anonymous = host.Client(false)) { using var response = await anonymous.GetAsync($"/admin/storefront/refunds/{c.OrderId}/state"); Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); }
        using var client = host.Client();
        using (var other = await client.GetAsync($"/admin/storefront/refunds/{foreign.OrderId}/state")) Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        await Web07AdminHost.RevokeAsync(c, PermissionCodes.Report.Treasury.Manage);
        using var sources = await client.GetAsync($"/admin/storefront/refunds/{c.OrderId}/payout-sources?refundId=1"); Assert.Equal(HttpStatusCode.Forbidden, sources.StatusCode);
    }

    [Fact]
    public async Task Actual_admin_execution_uses_the_full_recorded_debit_and_ignores_client_amount_or_verified_store_actor_claims()
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture, prepaidQr: true);
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, true).CancelGuestAsync(await c.CancelRequestAsync());
        int refundId; await using (var db = c.Context()) refundId = (await db.Set<StorefrontRefund>().SingleAsync()).Id;
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ApproveAsync(c.OrderId, await c.RefundDecisionAsync(refundId, "Đã xác minh nguồn gốc tiền"));
        var debit = await Web07RecordedDebit.BankAsync(c, 230_000m);
        var command = await c.ExecutionRequestAsync(refundId, debit.Source, c.Seed.LegalEntityId, c.Seed.BankAccountId, debit.ExternalReference, debit.ActualExecutionAt);
        var body = JsonSerializer.SerializeToNode(command)!.AsObject(); body["Amount"] = 1; body["Verified"] = true; body["StoreId"] = int.MaxValue; body["ActorUserId"] = int.MaxValue;
        await using var host = await Web07AdminHost.StartAsync(c); using var client = host.Client(); await Web07AdminHost.CsrfAsync(client, c.OrderId);
        var before = await Web07RefundExecutionTests.FlowAsync(c);
        using var response = await client.PostAsJsonAsync($"/admin/storefront/refunds/{c.OrderId}/confirm-execution", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal(before, await Web07RefundExecutionTests.FlowAsync(c));
        await using var check = c.Context(); var execution = Assert.Single(await check.Set<StorefrontRefundEvidence>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
        Assert.Equal(230_000m, execution.Amount); Assert.Equal(c.Seed.UserId, execution.ConfirmedByUserId); Assert.Equal(c.StoreId, execution.StoreId);
    }
}
