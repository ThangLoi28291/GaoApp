using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Delivery;

[Collection("DeliveryD02"), Trait("Category", "DeliveryD02")]
public sealed class DeliveryD02HttpTests(DeliveryD02Fixture fixture)
{
    [Fact]
    public async Task Anonymous_and_missing_permission_cannot_use_known_QR()
    {
        using var c = await fixture.CaseAsync();
        using var anonymous = fixture.Web.Anonymous(c.Account.Store);
        using var denied = await anonymous.Http.GetAsync("/admin/api/deliveries/lookup?key=" + c.Detail.LookupToken);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        var limited = await fixture.Web.AddAccountAsync(c.Account.Store, PermissionCodes.Admin.DashboardView);
        using var client = await fixture.Web.LoginAsync(limited);
        using var noPermission = await client.Http.GetAsync("/admin/api/deliveries/lookup?key=" + c.Detail.LookupToken);
        Assert.Equal(HttpStatusCode.Forbidden, noPermission.StatusCode);
    }
    [Fact]
    public async Task Foreign_store_cannot_read_lookup_history_or_edit_delivery()
    {
        using var c = await fixture.CaseAsync();
        var foreign = await fixture.Web.AddAccountAsync(fixture.Web.Stores[1], "*");
        using var client = await fixture.Web.LoginAsync(foreign);
        foreach (var path in new[] { "/" + c.Detail.Id, "/" + c.Detail.Id + "/history", "/lookup?key=" + c.Detail.LookupToken, "/lookup?key=" + c.Detail.Code })
        {
            using var response = await client.Http.GetAsync("/admin/api/deliveries" + path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        using var edit = await client.Http.PostAsJsonAsync("/admin/api/deliveries/" + c.Detail.Id + "/recipient", c.Change());
        Assert.Equal(HttpStatusCode.NotFound, edit.StatusCode);
        var list = await client.JsonAsync(HttpMethod.Get, "/admin/api/deliveries");
        Assert.DoesNotContain(list.EnumerateArray(), x => x.GetProperty("id").GetInt32() == c.Detail.Id);
    }
    [Fact]
    public async Task Detail_lookup_code_and_history_show_persisted_snapshot_and_server_actor()
    {
        using var c = await fixture.CaseAsync();
        var changed = await c.PostAsync(c.Change());
        foreach (var path in new[] { "/" + c.Detail.Id, "/lookup?key=" + c.Detail.LookupToken, "/lookup?key=" + c.Detail.Code })
        {
            var detail = await c.Client.JsonAsync(HttpMethod.Get, "/admin/api/deliveries" + path);
            Assert.Equal(changed.Version, detail.GetProperty("version").GetString());
            Assert.Equal(2, detail.GetProperty("revision").GetInt32());
            Assert.Equal(c.Account.Store.TerminalId, detail.GetProperty("createdTerminalId").GetInt32());
        }
        var history = await c.Client.JsonAsync(HttpMethod.Get, "/admin/api/deliveries/" + c.Detail.Id + "/history");
        Assert.Equal(2, history.GetArrayLength());
        Assert.Equal(c.Account.UserId, history[1].GetProperty("actorUserId").GetInt32());
        var snapshot = JsonDocument.Parse(history[0].GetProperty("snapshotJson").GetString()!).RootElement;
        Assert.Equal(c.Detail.RecipientName, snapshot.GetProperty("recipientName").GetString());
    }
    [Fact]
    public async Task View_only_cannot_mutate_and_permissions_revocation_takes_effect()
    {
        using var c = await fixture.CaseAsync();
        var viewer = await fixture.Web.AddAccountAsync(c.Account.Store, PermissionCodes.Delivery.View);
        using var client = await fixture.Web.LoginAsync(viewer);
        using var view = await client.Http.GetAsync("/admin/api/deliveries/" + c.Detail.Id);
        Assert.Equal(HttpStatusCode.OK, view.StatusCode);
        using var edit = await client.Http.PostAsJsonAsync("/admin/api/deliveries/" + c.Detail.Id + "/recipient", c.Change());
        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);
        await using (var db = c.Context())
        {
            var grants = await db.RolePermissions.Where(x => x.RoleId == viewer.RoleId).ToListAsync();
            db.RolePermissions.RemoveRange(grants); await db.SaveChangesAsync();
        }
        using var revoked = await client.Http.GetAsync("/admin/api/deliveries/" + c.Detail.Id);
        Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Inactive_source_warehouse_or_owner_is_blocked_over_HTTP(bool warehouse)
    {
        using var c = await fixture.CaseAsync(); await using var db = c.Context();
        try
        {
            if (warehouse)
            {
                var item = await db.Warehouses.SingleAsync(x => x.Id == c.Detail.SourceWarehouseId); item.IsActive = false;
            }
            else
            {
                var item = await db.LegalEntities.SingleAsync(x => x.Id == c.Detail.SourceLegalEntityId); item.IsActive = false;
            }
            await db.SaveChangesAsync();
            using var response = await c.Client.Http.GetAsync("/admin/api/deliveries/lookup?key=" + c.Detail.LookupToken);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using var mutation = await c.Client.Http.PostAsJsonAsync("/admin/api/deliveries/" + c.Detail.Id + "/recipient", c.Change());
            Assert.Equal(HttpStatusCode.Forbidden, mutation.StatusCode);
        }
        finally
        {
            if (warehouse) (await db.Warehouses.SingleAsync(x => x.Id == c.Detail.SourceWarehouseId)).IsActive = true;
            else (await db.LegalEntities.SingleAsync(x => x.Id == c.Detail.SourceLegalEntityId)).IsActive = true;
            await db.SaveChangesAsync();
        }
    }
    [Theory]
    [InlineData("storeId")]
    [InlineData("actorUserId")]
    [InlineData("sourceWarehouseId")]
    [InlineData("bankVerified")]
    public async Task Unknown_client_authority_fields_are_rejected(string field)
    {
        using var c = await fixture.CaseAsync();
        var body = JsonSerializer.SerializeToNode(c.Change(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        body[field] = 999;
        using var response = await c.Client.Http.PostAsJsonAsync("/admin/api/deliveries/" + c.Detail.Id + "/recipient", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var db = c.Context(); Assert.Equal(1, (await db.DeliveryOrders.SingleAsync(x => x.Id == c.Detail.Id)).Revision);
    }
    [Fact]
    public async Task CSRF_invalid_key_version_and_recipient_are_rejected()
    {
        using var c = await fixture.CaseAsync(); var path = "/admin/api/deliveries/" + c.Detail.Id + "/recipient";
        var csrf = c.Client.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();
        c.Client.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using (var response = await c.Client.Http.PostAsJsonAsync(path, c.Change())) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        c.Client.Http.DefaultRequestHeaders.Add("RequestVerificationToken", csrf);
        foreach (var body in new[] { c.Change(key: Guid.Empty), c.Change(version: "fake"), c.Change(name: "") })
        {
            using var response = await c.Client.Http.PostAsJsonAsync(path, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
    [Fact]
    public async Task Same_key_cannot_replay_another_actor_response()
    {
        using var c = await fixture.CaseAsync(); var request = c.Change(); await c.PostAsync(request);
        var other = await fixture.Web.AddAccountAsync(c.Account.Store, PermissionCodes.Delivery.View, PermissionCodes.Delivery.Create);
        using var client = await fixture.Web.LoginAsync(other);
        using var response = await client.Http.PostAsJsonAsync("/admin/api/deliveries/" + c.Detail.Id + "/recipient", request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
    [Fact]
    public async Task Human_code_lookup_is_rate_limited()
    {
        using var c = await fixture.CaseAsync();
        for (var i = 0; i < 30; i++)
        {
            using var response = await c.Client.Http.GetAsync("/admin/api/deliveries/lookup?key=" + c.Detail.Code);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using var limited = await c.Client.Http.GetAsync("/admin/api/deliveries/lookup?key=" + c.Detail.Code);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        var second = await fixture.Web.AddAccountAsync(c.Account.Store, PermissionCodes.Delivery.View);
        using var secondClient = await fixture.Web.LoginAsync(second);
        using var unaffected = await secondClient.Http.GetAsync("/admin/api/deliveries/lookup?key=" + c.Detail.Code);
        Assert.Equal(HttpStatusCode.OK, unaffected.StatusCode);
    }
}
