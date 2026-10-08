using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Domain.Delivery;
using GaoApp.Tests.Security;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Delivery;

[Collection("DeliveryD06"), Trait("Category", "DeliveryD06")]
public sealed class DeliveryD06HttpTests(DeliveryD02Fixture fixture)
{
    [Fact]
    public async Task H01_Reads_require_fresh_view_and_do_not_expose_financial_cost_or_change_any_fact()
    {
        using var c = await DeliveryD06Case.CreateAsync(fixture); await c.CreateDraftAsync();
        var viewer = await fixture.Web.AddAccountAsync(c.Actor.Store, PermissionCodes.Delivery.View);
        using var client = await fixture.Web.LoginAsync(viewer); var before = await c.SnapshotAsync();
        foreach (var path in new[] { "/admin/api/delivery-trips", c.Path() })
        {
            using var response = await client.Http.GetAsync(path); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore == true);
            var raw = await response.Content.ReadAsStringAsync();
            foreach (var forbidden in new[] { "unitCost", "costAmount", "password", "payment", "cashAmount" })
                Assert.DoesNotContain(forbidden, raw, StringComparison.OrdinalIgnoreCase);
        }
        using var options = await client.Http.GetAsync("/admin/api/delivery-trips/options");
        Assert.Equal(HttpStatusCode.Forbidden, options.StatusCode); Assert.Equal(before, await c.SnapshotAsync());
        await using (var db = c.Context())
        {
            db.RolePermissions.RemoveRange(await db.RolePermissions.Where(x => x.RoleId == viewer.RoleId).ToArrayAsync());
            await db.SaveChangesAsync();
        }
        using var denied = await client.Http.GetAsync(c.Path()); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Theory]
    [InlineData("handover")]
    [InlineData("depart")]
    [InlineData("reassign")]
    [InlineData("cancel-request")]
    public async Task H02_The_original_operation_rights_are_required_even_for_an_exact_completed_replay(string operation)
    {
        using var c = await DeliveryD06Case.CreateAsync(fixture); await c.CreateDraftAsync();
        var code = operation switch { "depart" => PermissionCodes.Delivery.RecordDeparture,
            "reassign" => PermissionCodes.Delivery.ReassignCourier, "cancel-request" => PermissionCodes.Delivery.Cancel,
            _ => PermissionCodes.Delivery.Handover };
        var actor = await fixture.Web.AddAccountAsync(c.Actor.Store, PermissionCodes.Delivery.View,
            PermissionCodes.Delivery.Handover, PermissionCodes.Delivery.RecordDeparture,
            PermissionCodes.Delivery.ReassignCourier, PermissionCodes.Delivery.Cancel);
        using var client = await fixture.Web.LoginAsync(actor);
        if (operation != "handover") await c.HandoverAsync();
        var detail = await c.DetailAsync();
        object request; string route;
        if (operation == "handover") { request = await c.HandoverRequestAsync(); route = c.Path("handover"); }
        else if (operation == "depart") { request = new DeliveryTripDepartureRequest(Guid.NewGuid(), detail.Version, await c.SelectionsAsync()); route = c.Path("depart"); }
        else if (operation == "reassign") { request = new DeliveryTripReassignRequest(Guid.NewGuid(), detail.Version, await c.SelectionsAsync(),
            c.OtherCourier.UserId, c.OtherCourier.UserId, true, "Actual whole-trip transfer"); route = c.Path("courier/reassign"); }
        else { request = new DeliveryTripCancellationRequest(Guid.NewGuid(), detail.Version,
            (await c.SelectionsAsync())[0].ExpectedVersion, "Actual recall"); route = c.Path("orders/" + c.Orders[0].Id + "/cancel-request"); }
        using (var success = await client.Http.PostAsJsonAsync(route, request)) Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        await using (var db = c.Context())
        {
            var permission = await db.Permissions.SingleAsync(x => x.Code == code);
            db.RolePermissions.RemoveRange(await db.RolePermissions.Where(x => x.RoleId == actor.RoleId && x.PermissionId == permission.Id).ToArrayAsync());
            await db.SaveChangesAsync();
        }
        var before = await c.SnapshotAsync();
        using var replay = await client.Http.PostAsJsonAsync(route, request);
        Assert.Equal(HttpStatusCode.Forbidden, replay.StatusCode); Assert.Equal(before, await c.SnapshotAsync());
    }

    [Fact]
    public async Task H03_Changed_draft_courier_replay_cannot_lose_its_original_Reassign_right_after_state_converges()
    {
        using var c = await DeliveryD06Case.CreateAsync(fixture); await c.CreateDraftAsync();
        var actor = await fixture.Web.AddAccountAsync(c.Actor.Store, PermissionCodes.Delivery.View,
            PermissionCodes.Delivery.Handover, PermissionCodes.Delivery.ReassignCourier);
        using var client = await fixture.Web.LoginAsync(actor);
        var request = new DeliveryTripEditDraftRequest(Guid.NewGuid(), (await c.DetailAsync()).Version,
            c.OtherCourier.UserId, await c.SelectionsAsync(), "Đổi người giao dự kiến");
        using (var response = await client.Http.PostAsJsonAsync(c.Path("draft"), request)) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using (var db = c.Context())
        {
            var receipt = await db.DeliveryTripCommandReceipts.SingleAsync(x => x.ClientRequestId == request.ClientRequestId);
            Assert.True(receipt.RequiredCapabilities.HasFlag(DeliveryCapability.ReassignCourier));
            var permission = await db.Permissions.SingleAsync(x => x.Code == PermissionCodes.Delivery.ReassignCourier);
            db.RolePermissions.RemoveRange(await db.RolePermissions.Where(x => x.RoleId == actor.RoleId && x.PermissionId == permission.Id).ToArrayAsync());
            await db.SaveChangesAsync();
        }
        var before = await c.SnapshotAsync();
        using var replay = await client.Http.PostAsJsonAsync(c.Path("draft"), request);
        Assert.Equal(HttpStatusCode.Forbidden, replay.StatusCode); Assert.Equal(before, await c.SnapshotAsync());
    }

    [Theory]
    [InlineData("storeId")]
    [InlineData("actorUserId")]
    [InlineData("sourceWarehouseId")]
    [InlineData("dispatchedQuantity")]
    [InlineData("costAmount")]
    [InlineData("recordedAtUtc")]
    [InlineData("departedAtUtc")]
    [InlineData("delivered")]
    public async Task H04_Client_authority_stock_money_and_time_fields_are_not_bound(string field)
    {
        using var c = await DeliveryD06Case.CreateAsync(fixture); await c.CreateDraftAsync();
        var request = JsonSerializer.SerializeToNode(await c.HandoverRequestAsync(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        request[field] = 123;
        var before = await c.SnapshotAsync();
        using var response = await c.ActorClient().PostAsJsonAsync(c.Path("handover"), request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(before, await c.SnapshotAsync());
    }

    [Fact]
    public async Task H05_Actual_CSRF_missing_key_version_reason_and_physical_confirmation_are_rejected_without_effects()
    {
        using var c = await DeliveryD06Case.CreateAsync(fixture); await c.CreateDraftAsync();
        var request = await c.HandoverRequestAsync(); var before = await c.SnapshotAsync();
        var http = c.ActorClient(); var token = http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();
        http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        try { using var response = await http.PostAsJsonAsync(c.Path("handover"), request); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); }
        finally { http.DefaultRequestHeaders.Add("RequestVerificationToken", token); }
        foreach (var invalid in new[] { request with { ClientRequestId = Guid.Empty }, request with { ExpectedTripVersion = "%%%" },
            request with { PhysicalReceiptConfirmed = false }, request with { ConfirmedCourierUserId = c.OtherCourier.UserId },
            request with { ConfirmationNote = new string('x', 501) } })
        {
            using var response = await http.PostAsJsonAsync(c.Path("handover"), invalid);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(before, await c.SnapshotAsync());
        }
        using var abandon = await http.PostAsJsonAsync(c.Path("abandon"), new DeliveryTripAbandonRequest(Guid.NewGuid(), request.ExpectedTripVersion, " "));
        Assert.Equal(HttpStatusCode.BadRequest, abandon.StatusCode); Assert.Equal(before, await c.SnapshotAsync());
    }

    [Fact]
    public async Task H06_Other_store_anonymous_unassigned_and_proxy_only_actors_cannot_mutate_custody()
    {
        using var c = await DeliveryD06Case.CreateAsync(fixture); await c.CreateDraftAsync();
        var otherStore = await fixture.Web.AddAccountAsync(fixture.Web.Stores[1], "*");
        var proxy = await fixture.Web.AddAccountAsync(c.Actor.Store, PermissionCodes.Delivery.View, PermissionCodes.Delivery.ReportOnBehalf);
        foreach (var actor in new[] { otherStore, proxy, c.OtherCourier })
        {
            using var client = await fixture.Web.LoginAsync(actor); var before = await c.SnapshotAsync();
            using var response = await client.Http.PostAsJsonAsync(c.Path("handover"), await c.HandoverRequestAsync());
            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound });
            Assert.Equal(before, await c.SnapshotAsync());
        }
        using var anonymous = fixture.Web.Anonymous(c.Actor.Store);
        using var denied = await anonymous.Http.PostAsJsonAsync(c.Path("handover"), await c.HandoverRequestAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("membership")]
    [InlineData("courier")]
    public async Task H07_Fresh_disabled_actor_membership_or_courier_is_rechecked_after_login(string fault)
    {
        using var c = await DeliveryD06Case.CreateAsync(fixture); await c.CreateDraftAsync();
        var actor = await fixture.Web.AddAccountAsync(c.Actor.Store, PermissionCodes.Delivery.View, PermissionCodes.Delivery.Handover);
        using var client = await fixture.Web.LoginAsync(actor); var request = await c.HandoverRequestAsync();
        await using (var db = c.Context())
        {
            if (fault == "membership") (await db.UserInStores.SingleAsync(x => x.UserId == actor.UserId)).IsActive = false;
            else (await db.Users.SingleAsync(x => x.Id == (fault == "courier" ? c.Courier.UserId : actor.UserId))).IsActive = false;
            await db.SaveChangesAsync();
        }
        var before = await c.SnapshotAsync();
        using var response = await client.Http.PostAsJsonAsync(c.Path("handover"), request);
        Assert.Equal(fault == "courier" ? HttpStatusCode.Conflict : HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(before, await c.SnapshotAsync());
    }

    [Fact]
    public async Task H08_Recorded_departure_keeps_shop_actor_separate_from_courier_and_read_refresh_does_not_finish_the_trip()
    {
        using var c = await DeliveryD06Case.CreateAsync(fixture); await c.CreateDraftAsync(); await c.HandoverAsync();
        var actor = await fixture.Web.AddAccountAsync(c.Actor.Store, PermissionCodes.Delivery.View, PermissionCodes.Delivery.RecordDeparture);
        using var client = await fixture.Web.LoginAsync(actor);
        var request = new DeliveryTripDepartureRequest(Guid.NewGuid(), (await c.DetailAsync()).Version, await c.SelectionsAsync());
        var beforeUtc = DateTimeOffset.UtcNow;
        using var response = await client.Http.PostAsJsonAsync(c.Path("depart"), request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = (await client.Http.GetFromJsonAsync<DeliveryTripDetailDto>(c.Path()))!;
        Assert.Equal(nameof(DeliveryTripState.Departed), detail.State); Assert.Equal(actor.UserId, detail.DepartedByUserId);
        Assert.NotEqual(actor.UserId, detail.CourierUserId); Assert.Equal(c.Courier.UserId, detail.CourierUserId);
        Assert.InRange(detail.DepartedAtUtc!.Value, beforeUtc, DateTimeOffset.UtcNow);
        var before = await c.SnapshotAsync();
        for (var index = 0; index < 3; index++)
        {
            using var get = await client.Http.GetAsync(c.Path()); Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        }
        Assert.Equal(before, await c.SnapshotAsync());
        Assert.All(detail.Orders, x => Assert.Equal(nameof(DeliveryState.Delivering), x.State));
    }

    [Theory]
    [InlineData("create")]
    [InlineData("draft")]
    [InlineData("handover")]
    [InlineData("depart")]
    [InlineData("reassign")]
    public async Task H09_Required_null_order_arrays_fail_with_typed_validation_in_direct_service_and_HTTP_without_effects(string operation)
    {
        using var c = await DeliveryD06Case.CreateAsync(fixture); await c.CreateDraftAsync();
        var version = (await c.DetailAsync()).Version; var key = Guid.NewGuid();
        object body = operation switch {
            "create" => new DeliveryTripCreateDraftRequest(key, c.Courier.UserId, null!),
            "draft" => new DeliveryTripEditDraftRequest(key, version, c.Courier.UserId, null!, "Required route"),
            "handover" => new DeliveryTripHandoverRequest(key, version, null!, c.Courier.UserId, true),
            "depart" => new DeliveryTripDepartureRequest(key, version, null!),
            _ => new DeliveryTripReassignRequest(key, version, null!, c.OtherCourier.UserId, c.OtherCourier.UserId, true, "Required route") };
        var before = await c.SnapshotAsync(); await using var db = c.Context(); var service = c.Service(db);
        Func<Task<DeliveryTripCommandAck>> direct = operation switch {
            "create" => () => service.CreateDraftAsync((DeliveryTripCreateDraftRequest)body),
            "draft" => () => service.EditDraftAsync(c.TripId, (DeliveryTripEditDraftRequest)body),
            "handover" => () => service.HandoverAsync(c.TripId, (DeliveryTripHandoverRequest)body),
            "depart" => () => service.DepartAsync(c.TripId, (DeliveryTripDepartureRequest)body),
            _ => () => service.ReassignCourierAsync(c.TripId, (DeliveryTripReassignRequest)body) };
        var rule = await Assert.ThrowsAsync<DeliveryFoundationException>(direct);
        Assert.Equal(400, rule.StatusCode); Assert.Equal("ORDERS_INVALID", rule.Code);
        Assert.Equal(before, await c.SnapshotAsync());
        var route = operation == "create" ? "/admin/api/delivery-trips" : c.Path(operation == "reassign" ? "courier/reassign" : operation);
        using var response = await c.ActorClient().PostAsJsonAsync(route, body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ORDERS_INVALID", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(before, await c.SnapshotAsync());
    }
}

internal static class D06HttpAccess
{
    internal static HttpClient ActorClient(this DeliveryD06Case value) => value.Orders[0].Client.Http;
}
