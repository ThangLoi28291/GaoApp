using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Domain.Delivery;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Delivery;

[Collection("DeliveryD07"), Trait("Category", "DeliveryD07")]
public sealed class DeliveryD07HttpTests(DeliveryD02Fixture fixture)
{
    [Fact]
    public async Task H01_Assigned_courier_Web_and_staff_Phone_AtStore_keep_actor_and_reported_money_separate()
    {
        using var c = await DeliveryD07Case.CreateAsync(fixture);
        using var courier = await fixture.Web.LoginAsync(c.Courier);
        var stock = await c.StockAndFinanceAsync(); var source = await c.SourceAsync(); var other = await c.OtherOrderAsync();
        var own = (await c.RequestAsync()) with { ReportSource = "WebByCourier", ReportedCashText = "20000" };
        await Ok(courier.Http, c.Path(), own);
        var detail = (await courier.Http.GetFromJsonAsync<DeliveryTripDetailDto>(c.Trip.Path()))!;
        Assert.True(detail.OutcomeCapabilities.CanReportOwn); Assert.False(detail.OutcomeCapabilities.CanReportOnBehalf);
        var saved = detail.Orders.Single(x => x.DeliveryOrderId == c.Id()).Outcome!;
        Assert.Equal(c.Courier.UserId, saved.ActorUserId); Assert.Equal("WebByCourier", saved.ReportSource);
        foreach (var reportSource in new[] { "Phone", "AtStore" })
        {
            await Ok(c.Order().Client.Http, c.Path(), (await c.RequestAsync(outcome: "NotDelivered", delivered: "0")) with {
                ReportSource = reportSource, ReportedCashText = "15000", ReportedTransferText = "12000" });
            detail = await c.Trip.DetailAsync(); saved = detail.Orders.Single(x => x.DeliveryOrderId == c.Id()).Outcome!;
            Assert.Equal(c.Actor.UserId, saved.ActorUserId); Assert.Equal(c.Courier.UserId, saved.CourierUserId);
            Assert.Equal(reportSource, saved.ReportSource); Assert.Equal("15000", saved.ReportedCashText);
            Assert.Equal("12000", saved.ReportedTransferText); Assert.True(saved.IsCurrentValid);
        }
        using var get = await courier.Http.GetAsync(c.Trip.Path()); Assert.True(get.Headers.CacheControl?.NoStore == true);
        Assert.Equal(stock, await c.StockAndFinanceAsync()); Assert.Equal(source, await c.SourceAsync());
        Assert.Equal(other, await c.OtherOrderAsync());
    }

    [Theory]
    [InlineData("outcome")]
    [InlineData("return-to-store")]
    public async Task H02_Completed_replay_requires_its_original_fresh_report_right(string operation)
    {
        using var c = await DeliveryD07Case.CreateAsync(fixture);
        var actor = await fixture.Web.AddAccountAsync(c.Actor.Store, PermissionCodes.Delivery.View, PermissionCodes.Delivery.ReportOnBehalf);
        using var client = await fixture.Web.LoginAsync(actor);
        object request = operation == "outcome" ? await c.RequestAsync() : await c.ReturnRequestAsync();
        var route = operation == "outcome" ? c.Path() : c.Trip.Path("return-to-store");
        await Ok(client.Http, route, request);
        await using (var db = c.Context())
        {
            var permission = await db.Permissions.SingleAsync(x => x.Code == PermissionCodes.Delivery.ReportOnBehalf);
            db.RolePermissions.RemoveRange(await db.RolePermissions.Where(x => x.RoleId == actor.RoleId && x.PermissionId == permission.Id).ToArrayAsync());
            await db.SaveChangesAsync();
        }
        var before = await c.SnapshotAsync();
        await Status(client.Http, route, request, HttpStatusCode.Forbidden, "DELIVERY_FORBIDDEN");
        Assert.Equal(before, await c.SnapshotAsync());
    }

    [Fact]
    public async Task H03_Anonymous_other_store_unassigned_and_view_only_actors_cannot_report_or_claim_arrival()
    {
        using var c = await DeliveryD07Case.CreateAsync(fixture); var request = await c.RequestAsync();
        var view = await fixture.Web.AddAccountAsync(c.Actor.Store, PermissionCodes.Delivery.View);
        var otherStore = await fixture.Web.AddAccountAsync(fixture.Web.Stores[1], "*");
        foreach (var actor in new[] { view, c.Trip.OtherCourier, otherStore })
        {
            using var client = await fixture.Web.LoginAsync(actor); var before = await c.SnapshotAsync();
            await Status(client.Http, c.Path(), request,
                actor == otherStore ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden,
                actor == otherStore ? "TRIP_NOT_FOUND" : "DELIVERY_FORBIDDEN");
            await Status(client.Http, c.Trip.Path("return-to-store"), await c.ReturnRequestAsync(),
                actor == otherStore ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden,
                actor == otherStore ? "TRIP_NOT_FOUND" : "DELIVERY_FORBIDDEN");
            Assert.Equal(before, await c.SnapshotAsync());
        }
        using var anonymous = fixture.Web.Anonymous(c.Actor.Store);
        using var denied = await anonymous.Http.PostAsJsonAsync(c.Path(), request);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var reader = await fixture.Web.LoginAsync(view);
        var read = (await reader.Http.GetFromJsonAsync<DeliveryTripDetailDto>(c.Trip.Path()))!;
        Assert.False(read.OutcomeCapabilities.CanReportOwn); Assert.False(read.OutcomeCapabilities.CanReportOnBehalf);
        Assert.False(read.OutcomeCapabilities.CanRecordReturn); Assert.All(read.Orders, x => Assert.False(x.CanReportOutcome));
    }

    [Fact]
    public async Task H04_CSRF_and_client_authority_fields_are_rejected_without_report_or_arrival_effects()
    {
        using var c = await DeliveryD07Case.CreateAsync(fixture); var request = await c.RequestAsync();
        var before = await c.SnapshotAsync(); var http = c.Order().Client.Http;
        var token = http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();
        http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        try { using var response = await http.PostAsJsonAsync(c.Path(), request); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); }
        finally { http.DefaultRequestHeaders.Add("RequestVerificationToken", token); }
        foreach (var field in new[] { "storeId", "actorUserId", "recordedAtUtc", "sourceWarehouseId", "costAmount" })
        {
            var body = JsonSerializer.SerializeToNode(request, new JsonSerializerOptions(JsonSerializerDefaults.Web))!; body[field] = 123;
            using var response = await http.PostAsJsonAsync(c.Path(), body); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.Equal(before, await c.SnapshotAsync());
    }

    [Fact]
    public async Task H05_Typed_source_courier_money_reason_line_time_and_physical_return_validation_preserve_the_snapshot()
    {
        using var c = await DeliveryD07Case.CreateAsync(fixture); var request = await c.RequestAsync();
        var before = await c.SnapshotAsync(); var http = c.Order().Client.Http;
        var invalid = new (DeliveryOutcomeRequest Body, string Code)[] {
            (request with { ReportSource = "WebByCourier" }, "PROXY_SOURCE_INVALID"),
            (request with { ReportedCourierUserId = c.Trip.OtherCourier.UserId }, "COURIER_MISMATCH"),
            (request with { ReportedCashText = "1.5" }, "MONEY_INVALID"),
            (request with { Outcome = "NotDelivered", Reason = " ", Lines = [new(request.Lines[0].LineId, "0", "4")] }, "REASON_REQUIRED"),
            (request with { Lines = [] }, "LINE_INVALID"),
            (request with { CustomerReceivedAtUtc = DateTimeOffset.UtcNow.AddDays(1) }, "CUSTOMER_TIME_INVALID") };
        foreach (var row in invalid) { await Status(http, c.Path(), row.Body, HttpStatusCode.BadRequest, row.Code); Assert.Equal(before, await c.SnapshotAsync()); }
        using var courier = await fixture.Web.LoginAsync(c.Courier);
        await Status(courier.Http, c.Path(), request, HttpStatusCode.BadRequest, "REPORT_SOURCE_INVALID");
        var arrival = await c.ReturnRequestAsync();
        foreach (var row in new[] { (arrival with { PhysicalReturnConfirmed = false }, "PHYSICAL_RETURN_REQUIRED"),
            (arrival with { ConfirmedCourierUserId = c.Trip.OtherCourier.UserId }, "COURIER_MISMATCH"),
            (arrival with { ConfirmationNote = new string('x', 501) }, "NOTE_INVALID") })
            await Status(http, c.Trip.Path("return-to-store"), row.Item1, HttpStatusCode.BadRequest, row.Item2);
        Assert.Equal(before, await c.SnapshotAsync());
    }

    [Theory]
    [InlineData("user")]
    [InlineData("membership")]
    [InlineData("courier")]
    public async Task H06_Actor_cookie_and_courier_eligibility_are_rechecked_after_login(string fault)
    {
        using var c = await DeliveryD07Case.CreateAsync(fixture);
        var actor = await fixture.Web.AddAccountAsync(c.Actor.Store, PermissionCodes.Delivery.View, PermissionCodes.Delivery.ReportOnBehalf);
        using var client = await fixture.Web.LoginAsync(actor); var request = await c.RequestAsync();
        await using (var db = c.Context())
        {
            if (fault == "membership") (await db.UserInStores.SingleAsync(x => x.UserId == actor.UserId)).IsActive = false;
            else (await db.Users.SingleAsync(x => x.Id == (fault == "courier" ? c.Courier.UserId : actor.UserId))).IsActive = false;
            await db.SaveChangesAsync();
        }
        var before = await c.SnapshotAsync();
        using var response = await client.Http.PostAsJsonAsync(c.Path(), request);
        Assert.Equal(fault == "courier" ? HttpStatusCode.Conflict : HttpStatusCode.Unauthorized, response.StatusCode);
        if (fault == "courier") Assert.Equal("COURIER_NOT_ELIGIBLE", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(before, await c.SnapshotAsync());
    }

    private static async Task Ok(HttpClient client, string path, object body)
    { using var response = await client.PostAsJsonAsync(path, body); Assert.Equal(HttpStatusCode.OK, response.StatusCode); }
    private static async Task Status(HttpClient client, string path, object body, HttpStatusCode status, string code)
    {
        using var response = await client.PostAsJsonAsync(path, body); Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }
}
