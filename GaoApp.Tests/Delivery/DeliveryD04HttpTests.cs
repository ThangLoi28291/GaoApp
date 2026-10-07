using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Delivery;

[Collection("DeliveryD04"), Trait("Category", "DeliveryD04")]
public sealed class DeliveryD04HttpTests(DeliveryD02Fixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task H01_All_nine_endpoints_require_identity_and_delivery_view_even_with_known_QR()
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        using var anonymous = fixture.Web.Anonymous(c.Account.Store);
        var limited = await c.ActorAsync(PermissionCodes.Delivery.Pick, PermissionCodes.Delivery.ApproveChanges);
        using var client = limited.Client;
        foreach (var path in new[] { c.Path(), c.Path("replacement-options") })
        {
            using var unauthenticated = await anonymous.Http.GetAsync(path);
            using var denied = await client.Http.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        foreach (var command in Bodies(c, (await c.GetAsync()).Delivery.Version))
        {
            await c.ErrorAsync(command.Operation, command.Body, HttpStatusCode.Unauthorized, client: anonymous);
            await c.ErrorAsync(command.Operation, command.Body, HttpStatusCode.Forbidden, client: client);
        }
    }

    [Fact]
    public async Task H02_All_commands_require_their_own_permission_and_antiforgery()
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        var viewer = await c.ActorAsync(PermissionCodes.Delivery.View);
        using var client = viewer.Client;
        var version = (await c.GetAsync()).Delivery.Version;
        foreach (var command in Bodies(c, version))
            await c.ErrorAsync(command.Operation, command.Body, HttpStatusCode.Forbidden, client: client);
        var token = c.Client.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();
        c.Client.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        try
        {
            foreach (var command in Bodies(c, version))
                await c.ErrorAsync(command.Operation, command.Body, HttpStatusCode.BadRequest);
        }
        finally { c.Client.Http.DefaultRequestHeaders.Add("RequestVerificationToken", token); }
        await using var db = c.Source.Context();
        Assert.Equal(1, await db.DeliveryRevisions.CountAsync(x => x.DeliveryOrderId == c.Id));
    }

    [Fact]
    public async Task H02_Fresh_permission_revocation_blocks_new_intent_and_exact_replay()
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        var initial = await c.GetAsync();
        var request = new DeliveryPickingEnvelope(Guid.NewGuid(), initial.Delivery.Version);
        await c.CommandAsync("claim", request);
        await using (var db = c.Source.Context())
        {
            var permissionId = await db.Permissions.Where(x => x.Code == PermissionCodes.Delivery.Pick).Select(x => x.Id).SingleAsync();
            var grant = await db.RolePermissions.SingleAsync(x => x.RoleId == c.Account.RoleId && x.PermissionId == permissionId);
            db.RolePermissions.Remove(grant); await db.SaveChangesAsync();
        }
        await c.ErrorAsync("claim", request, HttpStatusCode.Forbidden);
        var current = await c.GetAsync();
        Assert.False(current.Capabilities.CanReport);
        await c.ErrorAsync("report", new DeliveryPickingReportRequest(Guid.NewGuid(), current.Delivery.Version,
            [new(current.Lines[0].LineId, "2", null)]), HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task H02_Pick_and_ApproveChanges_are_distinct_authorities_for_each_command(bool pick)
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        var actor = await c.ActorAsync(PermissionCodes.Delivery.View,
            pick ? PermissionCodes.Delivery.Pick : PermissionCodes.Delivery.ApproveChanges);
        using var client = actor.Client;
        var detail = await c.GetAsync();
        var pickingCommands = new[] { "claim", "report", "submit" };
        foreach (var command in Bodies(c, detail.Delivery.Version)
            .Where(x => pickingCommands.Contains(x.Operation) != pick))
            await c.ErrorAsync(command.Operation, command.Body, HttpStatusCode.Forbidden, client: client);
        Assert.Equal(detail.Delivery.Version, (await c.GetAsync()).Delivery.Version);
    }

    [Fact]
    public async Task H03_Foreign_store_cannot_read_or_mutate_picking_with_known_identity()
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        using var foreign = await fixture.Web.LoginAsync(await fixture.Web.AddAccountAsync(fixture.Web.Stores[1], "*"));
        foreach (var path in new[] { c.Path(), c.Path("replacement-options") })
        {
            using var denied = await foreign.Http.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        }
        foreach (var command in Bodies(c, (await c.GetAsync()).Delivery.Version))
            await c.ErrorAsync(command.Operation, command.Body, HttpStatusCode.NotFound, client: foreign);
        await using var db = c.Source.Context();
        Assert.False(await db.DeliveryPickingWorks.AnyAsync(x => x.DeliveryOrderId == c.Id));
    }

    [Fact]
    public async Task H04_Legacy_foundation_Draft_remains_readable_but_cannot_be_claimed()
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture, operational: false);
        var detail = await c.GetAsync();
        Assert.False(detail.Capabilities.CanClaim);
        await c.ErrorAsync("claim", new DeliveryPickingEnvelope(Guid.NewGuid(), detail.Delivery.Version),
            HttpStatusCode.Conflict, "SOURCE_NOT_CONVERTED");
        await using var db = c.Source.Context();
        Assert.Equal(OrderStatus.Draft, await db.Orders.Where(x => x.Id == c.Source.CartId).Select(x => x.Status).SingleAsync());
        Assert.False(await db.DeliveryPickingWorks.AnyAsync(x => x.DeliveryOrderId == c.Id));
    }

    [Fact]
    public async Task H04_Eligible_other_counter_can_claim_after_origin_shift_closes_and_creator_retires()
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        var picker = await c.ActorAsync(PermissionCodes.Delivery.View, PermissionCodes.Delivery.Pick);
        using var client = picker.Client;
        await c.Client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/close", new { closingCashActual = 0 });
        await using (var db = c.Source.Context())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Users SET IsActive=0,IsDeleted=1 WHERE Id={c.Account.UserId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE POSTerminals SET IsActive=0,IsDeleted=1 WHERE Id={c.Account.Store.TerminalId}");
        }
        var claimed = await c.ClaimAsync(client);
        Assert.Equal("Picking", claimed.Delivery.State);
        Assert.Equal(picker.Account.UserId, claimed.PickerUserId);
        Assert.Equal(c.Account.Store.WarehouseId, claimed.Delivery.SourceWarehouseId);
        Assert.Equal(c.Account.UserId, claimed.Delivery.CreatedByUserId);
    }

    [Theory]
    [InlineData("actorUserId")]
    [InlineData("storeId")]
    [InlineData("unitPriceText")]
    [InlineData("priceTier")]
    [InlineData("approved")]
    public async Task H05_Nested_client_authority_fields_are_rejected_without_effect(string field)
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        var detail = await c.ClaimAsync();
        var request = new DeliveryPickingReportRequest(Guid.NewGuid(), detail.Delivery.Version, [new(detail.Lines[0].LineId, "2", null)]);
        var body = JsonSerializer.SerializeToNode(request, Json)!;
        body["lines"]![0]![field] = "forbidden";
        await c.ErrorAsync("report", body, HttpStatusCode.BadRequest);
        var unchanged = await c.GetAsync();
        Assert.Equal(detail.Delivery.Version, unchanged.Delivery.Version);
        Assert.Null(unchanged.Lines[0].ReportedQuantityText);
    }

    [Fact]
    public async Task H05_Empty_key_bad_version_numeric_quantity_and_top_level_authority_are_bad_requests()
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        var detail = await c.ClaimAsync();
        await c.ErrorAsync("report", new DeliveryPickingReportRequest(Guid.Empty, detail.Delivery.Version,
            [new(detail.Lines[0].LineId, "2", null)]), HttpStatusCode.BadRequest);
        await c.ErrorAsync("report", new DeliveryPickingReportRequest(Guid.NewGuid(), "AQ==",
            [new(detail.Lines[0].LineId, "2", null)]), HttpStatusCode.BadRequest);
        var request = new DeliveryPickingReportRequest(Guid.NewGuid(), detail.Delivery.Version, [new(detail.Lines[0].LineId, "2", null)]);
        var numeric = JsonSerializer.SerializeToNode(request, Json)!;
        numeric["lines"]![0]!["pickedQuantityText"] = 2;
        await c.ErrorAsync("report", numeric, HttpStatusCode.BadRequest);
        var unknown = JsonSerializer.SerializeToNode(request, Json)!;
        unknown["sourceWarehouseId"] = 999;
        await c.ErrorAsync("report", unknown, HttpStatusCode.BadRequest);
        Assert.Equal(detail.Delivery.Version, (await c.GetAsync()).Delivery.Version);
    }

    [Fact]
    public async Task H06_Another_employee_with_pick_permission_cannot_report_or_submit_assigned_work()
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        var detail = await c.ClaimAsync();
        var other = await c.ActorAsync(PermissionCodes.Delivery.View, PermissionCodes.Delivery.Pick);
        using var client = other.Client;
        await c.ErrorAsync("report", new DeliveryPickingReportRequest(Guid.NewGuid(), detail.Delivery.Version,
            [new(detail.Lines[0].LineId, "2", null)]), HttpStatusCode.Conflict, client: client);
        await c.ErrorAsync("submit", new DeliveryPickingEnvelope(Guid.NewGuid(), detail.Delivery.Version), HttpStatusCode.Conflict, client: client);
        Assert.Null((await c.GetAsync()).Lines[0].ReportedQuantityText);
    }

    [Fact]
    public async Task H07_Reassign_to_active_employee_does_not_grant_missing_pick_permission()
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        var detail = await c.ClaimAsync();
        var target = await c.ActorAsync(PermissionCodes.Delivery.View);
        using var client = target.Client;
        await using var db = c.Source.Context();
        var grants = await db.RolePermissions.Where(x => x.RoleId == target.Account.RoleId).Select(x => x.PermissionId).OrderBy(x => x).ToArrayAsync();
        await c.CommandAsync("reassign", new DeliveryPickingReassignRequest(Guid.NewGuid(), detail.Delivery.Version,
            target.Account.UserId, "Đổi người soạn"));
        var latest = await c.GetAsync(client);
        Assert.Equal(target.Account.UserId, latest.PickerUserId);
        Assert.False(latest.Capabilities.CanReport);
        await c.ErrorAsync("report", new DeliveryPickingReportRequest(Guid.NewGuid(), latest.Delivery.Version,
            [new(latest.Lines[0].LineId, "2", null)]), HttpStatusCode.Forbidden, client: client);
        Assert.Equal(grants, await db.RolePermissions.Where(x => x.RoleId == target.Account.RoleId).Select(x => x.PermissionId).OrderBy(x => x).ToArrayAsync());
    }

    [Fact]
    public async Task H08_Original_picker_replays_exact_ack_after_reassign_and_GET_returns_current_actor()
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        var initial = await c.GetAsync();
        var claim = new DeliveryPickingEnvelope(Guid.NewGuid(), initial.Delivery.Version);
        var first = await c.CommandAsync("claim", claim);
        var other = await c.ActorAsync(PermissionCodes.Delivery.View, PermissionCodes.Delivery.Pick);
        using var otherClient = other.Client;
        var claimed = await c.GetAsync();
        await c.CommandAsync("reassign", new DeliveryPickingReassignRequest(Guid.NewGuid(), claimed.Delivery.Version,
            other.Account.UserId, "Tiếp nhận công việc"));
        var replay = await c.CommandAsync("claim", claim);
        Assert.True(replay.Replayed);
        Assert.Equal(first with { Replayed = true }, replay);
        var latest = await c.GetAsync();
        Assert.Equal(other.Account.UserId, latest.PickerUserId);
        Assert.NotEqual(first.AppliedVersion, latest.Delivery.Version);
        Assert.False(latest.Capabilities.CanReport);
    }

    [Fact]
    public async Task H08_Receipt_identity_rejects_changed_actor_order_or_operation_with_the_same_GUID()
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        using var otherOrder = await DeliveryD04Case.CreateAsync(fixture);
        var initial = await c.GetAsync();
        var claim = new DeliveryPickingEnvelope(Guid.NewGuid(), initial.Delivery.Version);
        await c.CommandAsync("claim", claim);
        var other = await c.ActorAsync(PermissionCodes.Delivery.View, PermissionCodes.Delivery.Pick);
        using var client = other.Client;
        await c.ErrorAsync("claim", claim, HttpStatusCode.Conflict, "REQUEST_KEY_CONFLICT", client);
        var otherDetail = await otherOrder.GetAsync();
        await otherOrder.ErrorAsync("claim", claim with { ExpectedVersion = otherDetail.Delivery.Version },
            HttpStatusCode.Conflict, "REQUEST_KEY_CONFLICT", c.Client);
        await c.ErrorAsync("report", new DeliveryPickingReportRequest(claim.ClientRequestId, initial.Delivery.Version,
            [new(initial.Lines[0].LineId, "2", null)]), HttpStatusCode.Conflict, "REQUEST_KEY_CONFLICT");
        Assert.Null((await c.GetAsync()).Lines[0].ReportedQuantityText);
        Assert.Equal("Created", (await otherOrder.GetAsync()).Delivery.State);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task H07_Reassignment_rejects_inactive_or_foreign_target_without_changing_responsibility(bool inactive)
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        var initial = await c.ClaimAsync();
        var target = inactive ? await fixture.Web.AddAccountAsync(c.Account.Store, PermissionCodes.Delivery.View, PermissionCodes.Delivery.Pick)
            : await fixture.Web.AddAccountAsync(fixture.Web.Stores[1], "*");
        if (inactive)
        {
            await using var db = c.Source.Context();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserInStores SET IsActive=0 WHERE UserId={target.UserId} AND StoreId={target.Store.StoreId}");
        }
        await c.ErrorAsync("reassign", new DeliveryPickingReassignRequest(Guid.NewGuid(), initial.Delivery.Version,
            target.UserId, "Thay người"), HttpStatusCode.BadRequest, "PICKER_INVALID");
        var unchanged = await c.GetAsync();
        Assert.Equal(initial.Delivery.Version, unchanged.Delivery.Version);
        Assert.Equal(c.Account.UserId, unchanged.PickerUserId);
    }

    [Fact]
    public async Task H09_Stale_new_version_rejects_but_stored_same_request_replays_before_current_guards()
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        var claimed = await c.ClaimAsync();
        var report = new DeliveryPickingReportRequest(Guid.NewGuid(), claimed.Delivery.Version, [new(claimed.Lines[0].LineId, "2", null)]);
        var ack = await c.CommandAsync("report", report);
        await c.ErrorAsync("report", report with { ClientRequestId = Guid.NewGuid() }, HttpStatusCode.Conflict, "VERSION_CONFLICT");
        await c.SubmitAsync();
        var replay = await c.CommandAsync("report", report);
        Assert.Equal(ack with { Replayed = true }, replay);
        Assert.Equal("ReadyForHandover", (await c.GetAsync()).Delivery.State);
    }

    [Fact]
    public async Task H10_Awaiting_approval_A5_is_explicit_and_QR_link_uses_current_revision()
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        await c.ClaimAsync(); await c.ReportAsync("1", "Thiếu một sản phẩm");
        var submitted = await c.SubmitAsync();
        Assert.Equal("AwaitingApproval", submitted.Delivery.State);
        var html = await c.Client.Http.GetStringAsync("/admin/deliveries/" + c.Id + "/bill");
        Assert.Contains("CHỜ DUYỆT", WebUtility.HtmlDecode(html));
        Assert.Contains("revision=" + submitted.Delivery.Revision, WebUtility.HtmlDecode(html));
        Assert.Contains(submitted.Delivery.LookupToken, html);
    }

    [Theory]
    [InlineData(DeliveryState.HandedOver)]
    [InlineData(DeliveryState.Settled)]
    [InlineData(DeliveryState.Cancelled)]
    public async Task H11_Real_HTTP_denies_every_new_picking_command_after_handover_or_terminal_state(DeliveryState state)
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        await c.ClaimAsync();
        await using var db = c.Source.Context();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE DeliveryOrders SET State={(int)state} WHERE Id={c.Id}");
        var detail = await c.GetAsync();
        var before = await c.NonDeliveryEffectsAsync();
        foreach (var command in Bodies(c, detail.Delivery.Version))
            await c.ErrorAsync(command.Operation, command.Body, HttpStatusCode.Conflict);
        Assert.Equal(before, await c.NonDeliveryEffectsAsync());
        Assert.Equal(detail.Delivery.Version, (await c.GetAsync()).Delivery.Version);
    }

    private static IEnumerable<(string Operation, object Body)> Bodies(DeliveryD04Case c, string version)
    {
        var lineId = c.Source.Detail.Lines[0].Id;
        yield return ("claim", new DeliveryPickingEnvelope(Guid.NewGuid(), version));
        yield return ("report", new DeliveryPickingReportRequest(Guid.NewGuid(), version, [new(lineId, "2", null)]));
        yield return ("submit", new DeliveryPickingEnvelope(Guid.NewGuid(), version));
        yield return ("plan", new DeliveryPickingPlanRequest(Guid.NewGuid(), version, [new(lineId, "2", null, null)], [], "Đổi kế hoạch", "Khách đồng ý"));
        yield return ("approve", new DeliveryPickingApproveRequest(Guid.NewGuid(), version, [new(lineId, "2", null)], "Duyệt", "Khách đồng ý"));
        yield return ("reopen", new DeliveryPickingReopenRequest(Guid.NewGuid(), version, "Soạn lại"));
        yield return ("reassign", new DeliveryPickingReassignRequest(Guid.NewGuid(), version, c.Account.UserId, "Đổi người"));
    }
}
