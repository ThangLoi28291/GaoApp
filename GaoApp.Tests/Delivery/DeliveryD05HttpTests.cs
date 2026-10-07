using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Domain.Delivery;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Delivery;

[Collection("DeliveryD05"), Trait("Category", "DeliveryD05")]
public sealed class DeliveryD05HttpTests(DeliveryD02Fixture fixture)
{
    [Fact]
    public async Task H01_Quantity_view_and_picking_projection_do_not_disclose_costs_without_valuation_permission()
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture);
        await c.ReadyAsync(); await c.DispatchAsync(); await c.ReceiveAsync("2");
        var (viewer, client) = await c.Picking.ActorAsync(PermissionCodes.Delivery.View);
        using (client)
        {
            var before = await c.SnapshotAsync();
            using var response = await client.Http.GetAsync(c.Path());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl!.NoStore);
            var summary = (await response.Content.ReadFromJsonAsync<DeliveryInventorySummaryDto>())!;
            Assert.False(summary.CanViewCosts); Assert.Empty(summary.CostFragments);
            Assert.Equal("8", summary.DispatchedBaseQuantityText); Assert.Equal("2", summary.AcceptedReturnedBaseQuantityText);
            Assert.Equal("6", summary.StockOutsideBaseQuantityText); Assert.Null(summary.InCustodyBaseQuantityText);
            Assert.All(summary.Lines, line => { Assert.Null(line.DispatchedCostText); Assert.Null(line.AcceptedReturnedCostText); Assert.Null(line.InCustodyBaseQuantityText); });
            var projection = await c.Picking.GetAsync(client);
            Assert.NotNull(projection.Inventory); Assert.False(projection.Inventory.CanViewCosts);
            Assert.Empty(projection.Inventory.CostFragments);
            Assert.All(projection.Inventory.Lines, line => { Assert.Null(line.DispatchedCostText); Assert.Null(line.AcceptedReturnedCostText); });
            Assert.Equal(before, await c.SnapshotAsync());
        }
        var (_, costClient) = await c.Picking.ActorAsync(PermissionCodes.Delivery.View, PermissionCodes.Inventory.Valuation.View);
        using (costClient)
        {
            var summary = (await costClient.Http.GetFromJsonAsync<DeliveryInventorySummaryDto>(c.Path()))!;
            Assert.True(summary.CanViewCosts); Assert.Equal(2, summary.CostFragments.Count);
            var line = Assert.Single(summary.Lines); Assert.Equal("80000", line.DispatchedCostText); Assert.Equal("22000", line.AcceptedReturnedCostText);
            Assert.All(summary.CostFragments, part => Assert.Equal("Final", part.CostQuality));
        }
        await using (var db = c.Picking.Source.Context())
        {
            var grants = await db.RolePermissions.Where(x => x.RoleId == viewer.RoleId).ToArrayAsync();
            db.RolePermissions.RemoveRange(grants); await db.SaveChangesAsync();
        }
        using var revoked = await fixture.Web.LoginAsync(viewer);
        using var denied = await revoked.Http.GetAsync(c.Path()); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task H02_Cancel_requires_view_and_cancel_and_replays_without_second_release_or_finance_effects()
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture);
        await c.Picking.ClaimAsync(); await c.Picking.ReportAsync("8");
        var detail = await c.Picking.GetAsync();
        var request = new DeliveryInventoryCancelRequest(Guid.NewGuid(), detail.Delivery.Version, "Khách hủy trước bàn giao");
        foreach (var permissions in new[] { new[] { PermissionCodes.Delivery.View }, new[] { PermissionCodes.Delivery.Cancel } })
        {
            var (_, client) = await c.Picking.ActorAsync(permissions);
            using (client)
            {
                var before = await c.SnapshotAsync();
                using var response = await client.Http.PostAsJsonAsync(c.Path("cancel"), request);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); Assert.Equal(before, await c.SnapshotAsync());
            }
        }
        var (_, authorized) = await c.Picking.ActorAsync(PermissionCodes.Delivery.View, PermissionCodes.Delivery.Cancel);
        using (authorized)
        {
            var finance = await c.FinanceAsync();
            using var response = await authorized.Http.PostAsJsonAsync(c.Path("cancel"), request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var ack = (await response.Content.ReadFromJsonAsync<DeliveryInventoryCommandAck>())!;
            Assert.Equal("cancel", ack.Operation); Assert.False(ack.Replayed);
            var after = await c.SnapshotAsync();
            using var replay = await authorized.Http.PostAsJsonAsync(c.Path("cancel"), request);
            var repeated = (await replay.Content.ReadFromJsonAsync<DeliveryInventoryCommandAck>())!;
            Assert.True(repeated.Replayed); Assert.Equal(ack.AppliedVersion, repeated.AppliedVersion);
            Assert.Equal(after, await c.SnapshotAsync()); Assert.Equal(finance, await c.FinanceAsync());
            var summary = await c.GetAsync(); Assert.Equal("0", summary.HeldBaseQuantityText); Assert.False(summary.CanCancelBeforeHandover);
            await using var db = c.Picking.Source.Context();
            Assert.Equal(DeliveryState.Cancelled, (await db.DeliveryOrders.SingleAsync(x => x.Id == c.Id)).State);
            Assert.Empty(await db.DeliveryJournalEntries.Where(x => x.DeliveryOrderId == c.Id).ToArrayAsync());
        }
    }

    [Theory]
    [InlineData("storeId")]
    [InlineData("sourceWarehouseId")]
    [InlineData("sourceLegalEntityId")]
    [InlineData("actorUserId")]
    [InlineData("amount")]
    [InlineData("dispatchedQuantity")]
    public async Task H03_Unknown_authority_stock_or_money_fields_are_rejected_without_effects(string field)
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture);
        var detail = await c.Picking.GetAsync();
        var request = JsonSerializer.SerializeToNode(new DeliveryInventoryCancelRequest(Guid.NewGuid(), detail.Delivery.Version, "Hủy"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        request[field] = 123;
        var before = await c.SnapshotAsync();
        using var response = await c.Picking.Client.Http.PostAsJsonAsync(c.Path("cancel"), request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(before, await c.SnapshotAsync());
    }

    [Fact]
    public async Task H04_CSFR_invalid_version_key_and_missing_reason_fail_without_releasing_a_hold()
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture);
        await c.Picking.ClaimAsync(); await c.Picking.ReportAsync("8");
        var detail = await c.Picking.GetAsync(); var before = await c.SnapshotAsync();
        var header = c.Picking.Client.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();
        c.Picking.Client.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        try
        {
            using var response = await c.Picking.Client.Http.PostAsJsonAsync(c.Path("cancel"),
                new DeliveryInventoryCancelRequest(Guid.NewGuid(), detail.Delivery.Version, "Hủy"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally { c.Picking.Client.Http.DefaultRequestHeaders.Add("RequestVerificationToken", header); }
        foreach (var request in new[] {
            new DeliveryInventoryCancelRequest(Guid.Empty, detail.Delivery.Version, "Hủy"),
            new DeliveryInventoryCancelRequest(Guid.NewGuid(), "%%%", "Hủy"),
            new DeliveryInventoryCancelRequest(Guid.NewGuid(), detail.Delivery.Version, "") })
        {
            using var response = await c.Picking.Client.Http.PostAsJsonAsync(c.Path("cancel"), request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(before, await c.SnapshotAsync());
        }
    }

    [Fact]
    public async Task H05_Other_store_and_anonymous_actor_cannot_read_or_cancel_and_raw_posting_routes_are_absent()
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture);
        var account = await fixture.Web.AddAccountAsync(fixture.Web.Stores[1], "*");
        using var other = await fixture.Web.LoginAsync(account);
        var before = await c.SnapshotAsync();
        using var anonymous = fixture.Web.Anonymous(c.Picking.Account.Store);
        foreach (var (client, expected) in new[] { (other, HttpStatusCode.NotFound), (anonymous, HttpStatusCode.Unauthorized) })
        {
            using var get = await client.Http.GetAsync(c.Path()); Assert.Equal(expected, get.StatusCode);
            using var post = await client.Http.PostAsJsonAsync(c.Path("cancel"),
                new DeliveryInventoryCancelRequest(Guid.NewGuid(), (await c.Picking.GetAsync()).Delivery.Version, "Hủy"));
            Assert.Equal(expected, post.StatusCode);
        }
        foreach (var route in new[] { "dispatch", "receive", "return", "finalize" })
        {
            using var response = await c.Picking.Client.Http.PostAsJsonAsync(c.Path(route), new { quantity = 8, amount = 80000 });
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        Assert.Equal(before, await c.SnapshotAsync());
    }

    [Fact]
    public async Task H06_Public_quantity_view_cannot_invoke_server_only_residual_cost_selection()
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture);
        await c.ReadyAsync(); await c.DispatchAsync();
        var (viewer, client) = await c.Picking.ActorAsync(PermissionCodes.Delivery.View, PermissionCodes.Inventory.Valuation.View);
        using (client)
        {
            var before = await c.SnapshotAsync();
            await Assert.ThrowsAsync<DeliveryFoundationException>(() => c.ResidualAsync("6", viewer));
            Assert.Equal(before, await c.SnapshotAsync());
        }
    }

    [Fact]
    public async Task H07_Actual_stock_dispatch_blocks_cancel_even_before_future_courier_state_integration()
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture); await c.ReadyAsync(); await c.DispatchAsync();
        var detail = await c.Picking.GetAsync(); var before = await c.SnapshotAsync();
        using var response = await c.Picking.Client.Http.PostAsJsonAsync(c.Path("cancel"),
            new DeliveryInventoryCancelRequest(Guid.NewGuid(), detail.Delivery.Version, "Cannot cancel dispatched stock"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("CANCEL_NOT_ALLOWED", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(before, await c.SnapshotAsync()); Assert.False((await c.GetAsync()).CanCancelBeforeHandover);
    }

    [Fact]
    public async Task H08_Cost_permission_revocation_redacts_an_existing_authenticated_session_immediately()
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture); await c.ReadyAsync(); await c.DispatchAsync();
        var (actor, client) = await c.Picking.ActorAsync(PermissionCodes.Delivery.View, PermissionCodes.Inventory.Valuation.View);
        using (client)
        {
            var original = (await client.Http.GetFromJsonAsync<DeliveryInventorySummaryDto>(c.Path()))!;
            Assert.True(original.CanViewCosts); Assert.NotEmpty(original.CostFragments);
            await using (var db = c.Picking.Source.Context())
            {
                var grant = await db.RolePermissions.SingleAsync(x => x.RoleId == actor.RoleId && x.Permission.Code == PermissionCodes.Inventory.Valuation.View);
                db.RolePermissions.Remove(grant); await db.SaveChangesAsync();
            }
            var before = await c.SnapshotAsync();
            var redacted = (await client.Http.GetFromJsonAsync<DeliveryInventorySummaryDto>(c.Path()))!;
            Assert.False(redacted.CanViewCosts); Assert.Empty(redacted.CostFragments);
            Assert.Equal(original.DispatchedBaseQuantityText, redacted.DispatchedBaseQuantityText);
            Assert.Equal(original.Version, redacted.Version);
            Assert.All(redacted.Lines, line => { Assert.Null(line.DispatchedCostText); Assert.Null(line.AcceptedReturnedCostText); });
            Assert.Equal(before, await c.SnapshotAsync());
        }
    }

    [Theory]
    [InlineData(500)]
    [InlineData(501)]
    public async Task H09_Cancellation_reason_boundary_releases_a_saved_hold_once_or_rejects_without_effects(int length)
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture);
        await c.Picking.ClaimAsync(); await c.Picking.ReportAsync("8");
        var detail = await c.Picking.GetAsync(); var normalized = new string('L', length);
        var request = new DeliveryInventoryCancelRequest(Guid.NewGuid(), detail.Delivery.Version, " \t" + normalized + "\r\n ");
        var before = await c.SnapshotAsync(); var finance = await c.FinanceAsync();
        using var response = await c.Picking.Client.Http.PostAsJsonAsync(c.Path("cancel"), request);
        if (length == 501)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("REASON_REQUIRED", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
            Assert.Equal(before, await c.SnapshotAsync()); Assert.Equal(finance, await c.FinanceAsync());
            Assert.Equal("8", (await c.GetAsync()).HeldBaseQuantityText);
            Assert.Equal(nameof(DeliveryState.Picking), (await c.Picking.GetAsync()).Delivery.State);
            return;
        }
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ack = (await response.Content.ReadFromJsonAsync<DeliveryInventoryCommandAck>())!;
        Assert.False(ack.Replayed); Assert.Equal("cancel", ack.Operation);
        await using (var db = c.Picking.Source.Context())
        {
            var hold = Assert.Single(await db.InventoryReservations.Where(x => x.DeliveryOrderId == c.Id).ToArrayAsync());
            Assert.Equal(GaoApp.Domain.Enums.InventoryReservationStatus.Released, hold.Status);
            Assert.Equal(8m, hold.ReservedQty); Assert.Equal(normalized, hold.ReleaseNote); Assert.NotNull(hold.ReleasedAtUtc);
            var balance = await db.InventoryBalances.SingleAsync(x => x.ProductVariantId == hold.ProductVariantId && x.WarehouseId == hold.WarehouseId);
            Assert.Equal(0m, balance.ReservedQty); Assert.Equal(8m, balance.OnHandQty); Assert.Equal(80000m, balance.InventoryValue);
        }
        await AssertCancellationAudit(c, ack, normalized);
        var after = await c.SnapshotAsync();
        using var replay = await c.Picking.Client.Http.PostAsJsonAsync(c.Path("cancel"), request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var repeated = (await replay.Content.ReadFromJsonAsync<DeliveryInventoryCommandAck>())!;
        Assert.True(repeated.Replayed); Assert.Equal(ack.AppliedVersion, repeated.AppliedVersion);
        Assert.Equal(after, await c.SnapshotAsync()); Assert.Equal(finance, await c.FinanceAsync());
    }

    [Fact]
    public async Task H10_Created_cancellation_keeps_normalized_reason_in_flat_durable_audit_without_stock_or_finance_effects()
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture);
        var detail = await c.Picking.GetAsync(); var finance = await c.FinanceAsync();
        const string normalized = "Khách đổi kế hoạch trước khi soạn";
        var request = new DeliveryInventoryCancelRequest(Guid.NewGuid(), detail.Delivery.Version, " \t" + normalized + "\r\n ");
        using var response = await c.Picking.Client.Http.PostAsJsonAsync(c.Path("cancel"), request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ack = (await response.Content.ReadFromJsonAsync<DeliveryInventoryCommandAck>())!;
        Assert.False(ack.Replayed); await AssertCancellationAudit(c, ack, normalized);
        await using var db = c.Picking.Source.Context();
        Assert.Empty(await db.InventoryReservations.Where(x => x.DeliveryOrderId == c.Id).ToArrayAsync());
        Assert.Empty(await db.DeliveryJournalEntries.Where(x => x.DeliveryOrderId == c.Id).ToArrayAsync());
        var variant = await c.VariantAsync();
        var balance = await db.InventoryBalances.SingleAsync(x => x.ProductVariantId == variant);
        Assert.Equal(8m, balance.OnHandQty); Assert.Equal(0m, balance.ReservedQty); Assert.Equal(80000m, balance.InventoryValue);
        Assert.Equal(finance, await c.FinanceAsync());
        var after = await c.SnapshotAsync();
        using var replay = await c.Picking.Client.Http.PostAsJsonAsync(c.Path("cancel"), request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var repeated = (await replay.Content.ReadFromJsonAsync<DeliveryInventoryCommandAck>())!;
        Assert.True(repeated.Replayed); Assert.Equal(ack.AppliedVersion, repeated.AppliedVersion);
        Assert.Equal(after, await c.SnapshotAsync()); Assert.Equal(finance, await c.FinanceAsync());
    }

    private static async Task AssertCancellationAudit(DeliveryD05Case c, DeliveryInventoryCommandAck ack, string reason)
    {
        await using var db = c.Picking.Source.Context();
        var revision = Assert.Single(await db.DeliveryRevisions.Where(x => x.DeliveryOrderId == c.Id && x.Action == "inventory-cancel").ToArrayAsync());
        Assert.Equal(ack.AppliedRevision, revision.Revision); Assert.Equal(ack.AppliedVersion, revision.AggregateVersion);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(revision.SnapshotJson))), revision.SnapshotHash);
        using var snapshot = JsonDocument.Parse(revision.SnapshotJson);
        Assert.Equal(reason, snapshot.RootElement.GetProperty("inventoryReason").GetString());
        Assert.Equal(c.Id, snapshot.RootElement.GetProperty("id").GetInt32());
        Assert.Equal(c.Picking.Source.Detail.Code, snapshot.RootElement.GetProperty("code").GetString());
        Assert.Equal(nameof(DeliveryState.Cancelled), snapshot.RootElement.GetProperty("state").GetString());
        Assert.Equal(ack.AppliedRevision, snapshot.RootElement.GetProperty("revision").GetInt32());
        Assert.Equal(JsonValueKind.Array, snapshot.RootElement.GetProperty("lines").ValueKind);
        Assert.False(snapshot.RootElement.TryGetProperty("snapshot", out _));
        Assert.Single(await db.DeliveryCommandReceipts.Where(x => x.DeliveryOrderId == c.Id && x.Operation == "inventory-cancel").ToArrayAsync());
        var message = Assert.Single(await db.DeliveryOutboxMessages.Where(x => x.DeliveryOrderId == c.Id && x.Action == "inventory-cancel").ToArrayAsync());
        using var payload = JsonDocument.Parse(message.PayloadJson);
        Assert.False(payload.RootElement.TryGetProperty("inventoryReason", out _));
        Assert.DoesNotContain(reason, message.PayloadJson);
    }
}
