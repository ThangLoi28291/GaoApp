using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Domain.Enums;
using GaoApp.Tests.Security;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Delivery;

[Collection("DeliveryD08")]
public sealed class DeliverySalePriceTests(DeliveryD02Fixture fixture)
{
    private static string Path(int id) => $"/admin/api/deliveries/{id}/sale-prices";
    private static Task<DeliveryPickingDetailDto?> Detail(DeliveryD08Case c) => c.Client.Http.GetFromJsonAsync<DeliveryPickingDetailDto>($"/admin/api/deliveries/{c.Id}/picking");
    private static DeliverySalePriceRequest Change(DeliveryPickingDetailDto p, string price="17500") =>
        new(Guid.NewGuid(), p.Delivery.Version, [new(p.Lines.Single(x=>x.IsActive).LineId, price)], "Khách thống nhất lại giá bán");
    private static async Task<DeliverySalePriceAck> Save(DeliveryD08Case c, DeliverySalePriceRequest request)
    {
        using var response = await c.Client.Http.PostAsJsonAsync(Path(c.Id), request);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<DeliverySalePriceAck>())!;
    }
    private static async Task<string> Physical(DeliveryD08Case c)
    {
        await using var db = c.Context();
        return JsonSerializer.Serialize(new {
            balances = await db.InventoryBalances.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.OnHandQty,x.ReservedQty,x.InventoryValue}).ToArrayAsync(),
            transactions = await db.InventoryTransactions.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.QuantityChange,x.AfterQty}).ToArrayAsync(),
            journals = await db.DeliveryJournalEntries.AsNoTracking().Where(x=>x.DeliveryOrderId==c.Id).OrderBy(x=>x.Id).Select(x=>new{x.Id,x.BaseQuantity,x.MoneyAmount}).ToArrayAsync()
        });
    }

    [Fact]
    public async Task Price_can_be_edited_before_picking_and_is_retained_when_picking_is_approved()
    {
        using var order=await DeliveryD06Order.CreateAsync(fixture,quantity:4,distinctSource:true);
        var original=await order.Picking.GetAsync();
        Assert.Equal("Created",original.Delivery.State);Assert.True(original.SalePrices!.CanEdit);
        using(var response=await order.Client.Http.PostAsJsonAsync(Path(order.Id),Change(original)))
            Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
        var changed=await order.Picking.GetAsync();
        Assert.Equal("70000",changed.DraftTotalText);
        var html=WebUtility.HtmlDecode(await order.Client.Http.GetStringAsync($"/admin/deliveries/{order.Id}/bill"));
        Assert.Contains("17.500",html);Assert.Contains("70.000",html);Assert.DoesNotContain("Người soạn:",html);
        var ready=await order.ReadyAsync();
        Assert.Equal("70000",ready.ApprovedTotalText);
        await using var db=order.Picking.Source.Context();
        Assert.Equal(80000m,(await db.DeliveryPickingWorks.SingleAsync(x=>x.DeliveryOrderId==order.Id)).ApprovedTotal);
        Assert.Empty(await db.InventoryTransactions.Where(x=>x.ReferenceType==InventoryReferenceType.Delivery&&x.ReferenceId==order.Id.ToString()).ToArrayAsync());
    }

    [Fact]
    public async Task Dispatched_negative_stock_order_can_change_price_once_then_settle_partial_goods_at_new_price_without_second_export()
    {
        using var c = await DeliveryD08Case.CreateAsync(fixture, stock:0);
        var original = (await Detail(c))!;
        var staleClosing = await c.RequestAsync(delivered:2);
        var physical = await Physical(c);
        var request = Change(original);
        var ack = await Save(c,request);
        Assert.False(ack.Replayed);
        Assert.True((await Save(c,request)).Replayed);
        Assert.Equal(physical,await Physical(c));
        var updated = (await Detail(c))!;
        Assert.Equal("70000",updated.ApprovedTotalText);
        Assert.Equal("17500",updated.Lines.Single(x=>x.IsActive).UnitPriceText);
        Assert.Equal("80000",updated.QuotedTotalText);
        Assert.Equal(ack.AppliedVersion,updated.Delivery.Version);
        await c.RejectAsync(staleClosing,HttpStatusCode.Conflict,"VERSION_CONFLICT");
        var html = WebUtility.HtmlDecode(await c.Client.Http.GetStringAsync($"/admin/deliveries/{c.Id}/bill"));
        Assert.Contains("17.500",html); Assert.Contains("70.000",html); Assert.Contains("80.000",html);
        using(var duplicate=await c.Client.Http.PostAsJsonAsync(Path(c.Id),request with {Lines=[new(request.Lines[0].LineId,"18000")]}))
            Assert.Equal(HttpStatusCode.Conflict,duplicate.StatusCode);
        var closing = await c.RequestAsync(delivered:2);
        var settled = await c.ConfirmAsync(closing);
        Assert.Equal("35000",settled.Settlement.PayableText);
        var after = (await Detail(c))!;
        Assert.False(after.SalePrices!.CanEdit);
        using(var locked=await c.Client.Http.PostAsJsonAsync(Path(c.Id),Change(after)))
            Assert.Equal(HttpStatusCode.Conflict,locked.StatusCode);
        Assert.True((await Save(c,request)).Replayed);
        await using var db = c.Context();
        var source = await db.DeliveryOrderLines.AsNoTracking().SingleAsync(x=>x.DeliveryOrderId==c.Id);
        Assert.Equal(20000m,source.UnitPrice); Assert.Equal(80000m,source.Net);
        var sale = await db.Orders.Include(x=>x.Lines).Include(x=>x.Payments).SingleAsync(x=>x.Id==settled.Settlement.SaleOrderId);
        Assert.Equal(35000m,sale.GrandTotal); Assert.Equal(35000m,Assert.Single(sale.Payments).Amount);
        Assert.Equal(17500m,Assert.Single(sale.Lines).UnitPrice); Assert.Equal(20000m,sale.Lines.Single().OriginalUnitPrice);
        Assert.False(await db.InventoryTransactions.AnyAsync(x=>x.ReferenceType==InventoryReferenceType.Order&&x.ReferenceId==sale.Id.ToString()));
        Assert.Single(await db.DeliveryRevisions.Where(x=>x.DeliveryOrderId==c.Id&&x.Action=="sale-price-change").ToArrayAsync());
        Assert.Equal(2m,await db.DeliveryReturnedCostFragments.Where(x=>x.DeliveryOrderId==c.Id).SumAsync(x=>x.BaseQuantity));
    }

    [Fact]
    public async Task Price_changes_require_dedicated_permission_and_same_store_and_reject_invalid_or_stale_requests()
    {
        using var c=await DeliveryD08Case.CreateAsync(fixture);
        var detail=(await Detail(c))!;var command=Change(detail);var physical=await Physical(c);
        var viewer=await fixture.Web.AddAccountAsync(c.Closer.Store,PermissionCodes.Delivery.View,PermissionCodes.Delivery.ApproveChanges);
        using(var client=await fixture.Web.LoginAsync(viewer))
        {
            using var denied=await client.Http.PostAsJsonAsync(Path(c.Id),command);
            Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
            Assert.False((await client.Http.GetFromJsonAsync<DeliveryPickingDetailDto>($"/admin/api/deliveries/{c.Id}/picking"))!.SalePrices!.CanEdit);
        }
        using(var client=await fixture.Web.LoginAsync(await fixture.Web.AddAccountAsync(fixture.Web.Stores[1],PermissionCodes.Delivery.View,PermissionCodes.Delivery.EditSalePrice)))
        {
            using var foreign=await client.Http.PostAsJsonAsync(Path(c.Id),command);
            Assert.Equal(HttpStatusCode.NotFound,foreign.StatusCode);
        }
        foreach(var price in new[]{"-1","0","17,5","1.234","10000000000000000","NaN"})
        {
            using var invalid=await c.Client.Http.PostAsJsonAsync(Path(c.Id),command with {Lines=[new(command.Lines[0].LineId,price)]});
            Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        }
        using(var missingReason=await c.Client.Http.PostAsJsonAsync(Path(c.Id),command with {Reason=""}))Assert.Equal(HttpStatusCode.BadRequest,missingReason.StatusCode);
        Assert.Equal(physical,await Physical(c));
        await Save(c,command);
        using(var stale=await c.Client.Http.PostAsJsonAsync(Path(c.Id),command with {ClientRequestId=Guid.NewGuid()}))Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        Assert.Equal(physical,await Physical(c));
    }

    [Fact]
    public async Task Role_editor_can_grant_and_revoke_price_editing_while_preserving_session_invalidation()
    {
        using var c = await DeliveryD08Case.CreateAsync(fixture);
        var editor = await fixture.Web.AddAccountAsync(c.Closer.Store, PermissionCodes.Delivery.View);
        var admin = await SecurityAdministrationPlanATests.MakeAdmin(fixture.Web,
            await fixture.Web.AddAccountAsync(c.Closer.Store, "*"));
        using var adminClient = await fixture.Web.LoginAsync(admin);
        using var editorClient = await fixture.Web.LoginAsync(editor);
        int viewId, priceId;
        await using (var db = c.Context())
        {
            viewId = await db.Permissions.Where(x => x.Code == PermissionCodes.Delivery.View).Select(x => x.Id).SingleAsync();
            priceId = await db.Permissions.Where(x => x.Code == PermissionCodes.Delivery.EditSalePrice).Select(x => x.Id).SingleAsync();
        }
        var matrix = WebUtility.HtmlDecode(await adminClient.Http.GetStringAsync($"/Admin/RolePermissions?roleId={editor.RoleId}"));
        Assert.Contains(PermissionCodes.Delivery.EditSalePrice, matrix);
        Assert.Contains("Sửa giá bán đơn giao chưa chốt tiền", matrix);
        var before = (await editorClient.Http.GetFromJsonAsync<DeliveryPickingDetailDto>($"/admin/api/deliveries/{c.Id}/picking"))!;
        Assert.False(before.SalePrices!.CanEdit);
        var command = Change(before);
        using (var denied = await editorClient.Http.PostAsJsonAsync(Path(c.Id), command))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        async Task SetPermission(bool enabled)
        {
            var fields = new List<KeyValuePair<string, string>>
            {
                new("RoleId", editor.RoleId.ToString()), new("SelectedPermissionIds", viewId.ToString()),
                new("AdministratorPassword", admin.Password), new("Reason", "Cấp hoặc thu hồi quyền sửa giá giao hàng")
            };
            if (enabled) fields.Add(new("SelectedPermissionIds", priceId.ToString()));
            using var response = await adminClient.Http.PostAsync("/Admin/RolePermissions/Save", new FormUrlEncodedContent(fields));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            await using var db = c.Context();
            Assert.Equal(enabled, await db.RolePermissions.AnyAsync(x => x.RoleId == editor.RoleId && x.PermissionId == priceId));
        }

        await SetPermission(true);
        using (var oldSession = await editorClient.Http.GetAsync($"/admin/api/deliveries/{c.Id}/picking"))
            Assert.Equal(HttpStatusCode.Unauthorized, oldSession.StatusCode);
        using var grantedClient = await fixture.Web.LoginAsync(editor);
        var allowed = (await grantedClient.Http.GetFromJsonAsync<DeliveryPickingDetailDto>($"/admin/api/deliveries/{c.Id}/picking"))!;
        Assert.True(allowed.SalePrices!.CanEdit);
        Assert.False(allowed.Capabilities.CanApprove);
        using (var saved = await grantedClient.Http.PostAsJsonAsync(Path(c.Id), command))
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        await SetPermission(false);
        using (var revokedSession = await grantedClient.Http.PostAsJsonAsync(Path(c.Id), command))
            Assert.Equal(HttpStatusCode.Unauthorized, revokedSession.StatusCode);
        using var revokedClient = await fixture.Web.LoginAsync(editor);
        using (var deniedReplay = await revokedClient.Http.PostAsJsonAsync(Path(c.Id), command))
            Assert.Equal(HttpStatusCode.Forbidden, deniedReplay.StatusCode);
        var revoked = (await revokedClient.Http.GetFromJsonAsync<DeliveryPickingDetailDto>($"/admin/api/deliveries/{c.Id}/picking"))!;
        Assert.False(revoked.SalePrices!.CanEdit);
    }

    [Fact]
    public async Task Price_change_keeps_approved_discounts_and_all_unchanged_lines()
    {
        using var c=await DeliveryD08Case.CreateAsync(fixture,promotion:"combo");
        var before=(await Detail(c))!; var line=before.SalePrices!.Lines.First(x=>x.CanEdit);
        var unit=DeliveryD08Case.Number(line.UnitPriceText)+1000;
        var command=new DeliverySalePriceRequest(Guid.NewGuid(),before.Delivery.Version,[new(line.LineId,unit.ToString(System.Globalization.CultureInfo.InvariantCulture))],"Đổi giá, giữ ưu đãi cũ");
        var physical=await Physical(c);await Save(c,command);
        var after=(await Detail(c))!;
        var changed=after.SalePrices!.Lines.Single(x=>x.LineId==line.LineId);
        Assert.Equal(line.DiscountText,changed.DiscountText);
        Assert.Equal(DeliveryD08Case.Number(line.NetText)+DeliveryD08Case.Number(line.QuantityText)*1000,DeliveryD08Case.Number(changed.NetText));
        foreach(var other in before.SalePrices.Lines.Where(x=>x.LineId!=line.LineId))Assert.Equal(other.NetText,after.SalePrices.Lines.Single(x=>x.LineId==other.LineId).NetText);
        Assert.Equal(physical,await Physical(c));
    }

    [Fact]
    public async Task Concurrent_counter_close_and_repricing_cannot_mix_old_money_with_new_price()
    {
        using var c=await DeliveryD08Case.CreateAsync(fixture);
        var closing=await c.RequestAsync();var change=Change((await Detail(c))!);
        var responses=await Task.WhenAll(c.Client.Http.PostAsJsonAsync(Path(c.Id),change),c.Client.Http.PostAsJsonAsync(c.Path,closing));
        try
        {
            Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.OK);
            Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.Conflict);
            await using var db=c.Context();
            if(responses[0].IsSuccessStatusCode)
            {
                Assert.False(await db.DeliveryJournalEntries.AnyAsync(x=>x.DeliveryOrderId==c.Id&&x.Kind==GaoApp.Domain.Delivery.DeliveryJournalKind.Settlement));
                Assert.Equal("70000",(await c.ConfirmAsync(await c.RequestAsync())).Settlement.PayableText);
            }
            else
            {
                var result=(await responses[1].Content.ReadFromJsonAsync<DeliveryHandoverAck>())!;
                Assert.Equal("80000",result.Settlement.PayableText);
                Assert.False(await db.DeliveryRevisions.AnyAsync(x=>x.DeliveryOrderId==c.Id&&x.Action=="sale-price-change"));
            }
        }
        finally{foreach(var response in responses)response.Dispose();}
    }
}
