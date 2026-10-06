using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using static GaoApp.Tests.Delivery.DeliveryD03Support;

namespace GaoApp.Tests.Delivery;

[Collection("DeliveryD03"), Trait("Category", "DeliveryD03")]
public sealed class DeliveryD03SqlServerTests(DeliveryD02Fixture fixture)
{
    [Fact]
    public async Task Transfer_preserves_source_releases_reservations_creates_empty_cart_without_financial_or_stock_posting()
    {
        using var c = await fixture.CaseAsync(false);
        await using var db = c.Context();
        var cart=await db.Orders.Include(x=>x.Lines).SingleAsync(x=>x.Id==c.CartId);
        var balance=await db.InventoryBalances.SingleAsync(x=>x.WarehouseId==c.Account.Store.WarehouseId && x.ProductVariantId==c.Account.Store.VariantId);
        balance.ReservedQty+=2;
        db.InventoryReservations.Add(new(){StoreId=c.Account.Store.StoreId,WarehouseId=balance.WarehouseId,ProductVariantId=balance.ProductVariantId,ReferenceType=InventoryReferenceType.Order,ReferenceId=c.CartId.ToString(),ReferenceLineId=cart.Lines.Single().Id,ReservedQty=2,Status=InventoryReservationStatus.Active});
        await db.SaveChangesAsync();
        var before = await db.InventoryTransactions.CountAsync();
        var stock = await db.InventoryBalances.Select(x => x.OnHandQty).ToArrayAsync();
        var cash = await db.POSShiftCashTransactions.CountAsync();
        var payments = await db.OrderPayments.CountAsync();
        var debts = await db.Set<GaoApp.Domain.Entities.CustomerReceivableEntry>().CountAsync();
        var reward = await db.CustomerRewardLedgers.CountAsync();
        var result = await Create(c); db.ChangeTracker.Clear();
        Assert.Equal("Created", result.Delivery.State); Assert.Equal(40, result.Delivery.QuotedTotal);
        var source = await db.Orders.Include(x => x.Lines).SingleAsync(x => x.Id == c.CartId);
        Assert.Equal(OrderStatus.Cancelled, source.Status); Assert.Equal(0, source.PaidTotal); Assert.Equal(40, source.GrandTotal);
        Assert.Contains(result.Delivery.Code, source.Note); Assert.Equal(2, Assert.Single(source.Lines).Quantity);
        var next = await db.Orders.Include(x => x.Lines).SingleAsync(x => x.Id == result.NextCartId);
        Assert.Equal(OrderStatus.Draft, next.Status); Assert.Empty(next.Lines);
        var shift = await db.POSShifts.SingleAsync(x => x.Id == result.Delivery.CreatedShiftId);
        Assert.Equal(next.Id, shift.CurrentOrderId); Assert.Equal(shift.WarehouseId, result.Delivery.SourceWarehouseId);
        Assert.Equal(stock, await db.InventoryBalances.Select(x => x.OnHandQty).ToArrayAsync());
        Assert.Equal(before, await db.InventoryTransactions.CountAsync()); Assert.Equal(cash, await db.POSShiftCashTransactions.CountAsync());
        Assert.Equal(payments, await db.OrderPayments.CountAsync()); Assert.Equal(debts, await db.Set<GaoApp.Domain.Entities.CustomerReceivableEntry>().CountAsync());
        Assert.Equal(reward, await db.CustomerRewardLedgers.CountAsync());
        Assert.False(await db.InventoryReservations.AnyAsync(x => x.ReferenceId == c.CartId.ToString() && x.Status == InventoryReservationStatus.Active));
        Assert.Equal(InventoryReservationStatus.Released,(await db.InventoryReservations.SingleAsync(x=>x.ReferenceId==c.CartId.ToString())).Status);
        Assert.Equal(balance.ReservedQty-2,(await db.InventoryBalances.AsNoTracking().SingleAsync(x=>x.Id==balance.Id)).ReservedQty);
        Assert.Equal(1, await db.POSAuditLogs.CountAsync(x => x.OrderId == c.CartId && x.Action == "DELIVERY_CREATED"));
        Assert.Equal(1, await db.DeliveryOutboxMessages.CountAsync(x => x.DeliveryOrderId == result.Delivery.Id));
        Assert.Empty(await db.DeliveryJournalEntries.Where(x => x.DeliveryOrderId == result.Delivery.Id).ToListAsync());
        Assert.Empty(await db.DeliveryDispatchCostFragments.Where(x => x.DeliveryOrderId == result.Delivery.Id).ToListAsync());
    }
    [Fact]
    public async Task Concurrent_double_click_returns_one_exact_durable_result()
    {
        using var c = await fixture.CaseAsync(false); var request = await Request(c);
        var results = await Task.WhenAll(Create(c, request), Create(c, request));
        Assert.Equal(JsonSerializer.Serialize(results[0]), JsonSerializer.Serialize(results[1]));
        await using var db = c.Context(); Assert.Equal(1, await db.DeliveryOrders.CountAsync(x => x.SourceCartId == c.CartId));
        Assert.Equal(1, await db.Set<GaoApp.Domain.Entities.PosOperationReceipt>().CountAsync(x => x.OperationId == request.ClientRequestId));
    }
    [Fact]
    public async Task Two_keys_for_same_snapshot_only_one_transfer_commits()
    {
        using var c = await fixture.CaseAsync(false); var body = await Request(c);
        var responses = await Task.WhenAll(c.Client.Http.PostAsJsonAsync(CreatePath, body), c.Client.Http.PostAsJsonAsync(CreatePath, body with { ClientRequestId = Guid.NewGuid() }));
        try { Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict); }
        finally { foreach(var r in responses) r.Dispose(); }
        await using var db = c.Context(); Assert.Equal(1, await db.DeliveryOrders.CountAsync(x => x.SourceCartId == c.CartId));
    }
    [Fact]
    public async Task Same_key_changed_recipient_is_conflict_without_second_transfer()
    {
        using var c = await fixture.CaseAsync(false); var body = await Request(c); await Create(c, body);
        using var r = await c.Client.Http.PostAsJsonAsync(CreatePath, body with { RecipientName = "Người khác" });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode); Assert.Contains("REQUEST_KEY_CONFLICT", await r.Content.ReadAsStringAsync());
        await using var db = c.Context(); Assert.Equal(1, await db.DeliveryOrders.CountAsync(x => x.SourceCartId == c.CartId));
    }
    [Fact]
    public async Task Lost_response_replay_survives_restart_closed_shift_and_is_readable_at_another_counter()
    {
        using var c = await fixture.CaseAsync(false); var body = await Request(c); var result = await Create(c, body);
        await c.Client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/close", new { closingCashActual = 0 });
        await fixture.Web.RestartAsync(enableDeliveryOutbox:false);
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(await Create(c, body)));
        var viewer = await fixture.Web.AddAccountAsync(c.Account.Store with { TerminalId = fixture.Web.Stores[0].TerminalId }, GaoApp.Application.Common.Security.PermissionCodes.Delivery.View);
        using var other = await fixture.Web.LoginAsync(viewer);
        using var bill = await other.Http.GetAsync(result.BillUrl); Assert.Equal(HttpStatusCode.OK, bill.StatusCode);
        var lookup = await other.Http.GetFromJsonAsync<GaoApp.Application.DTOs.Delivery.DeliveryDetailDto>("/admin/api/deliveries/lookup?key=" + result.Delivery.Code);
        Assert.Equal(result.Delivery.Id, lookup!.Id); Assert.Equal(result.Delivery.CreatedTerminalId, lookup.CreatedTerminalId);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task SQL_failure_after_delivery_creation_or_next_cart_rolls_back_every_effect_and_same_key_can_retry(bool afterNextCart)
    {
        using var c = await fixture.CaseAsync(false); var request = await Request(c); var name = "CK_D03Fault_" + Guid.NewGuid().ToString("N");
        var table = afterNextCart ? "POSAuditLogs" : "Orders";
        await using var db = c.Context(); var count = await db.Orders.CountAsync();
        var reserved = await db.InventoryBalances.Select(x=>x.ReservedQty).ToArrayAsync();
        var condition = afterNextCart ? $"NOT (OrderId={c.CartId} AND Action='DELIVERY_CREATED')" : $"NOT (Id={c.CartId} AND Status=3)";
        await fixture.Web.Database.ExecuteAsync($"ALTER TABLE [{table}] ADD CONSTRAINT [{name}] CHECK ({condition});");
        try {
            using var failure = await c.Client.Http.PostAsJsonAsync(CreatePath, request); Assert.Equal(HttpStatusCode.InternalServerError, failure.StatusCode);
            Assert.Empty(await db.DeliveryOrders.Where(x=>x.SourceCartId==c.CartId).ToListAsync());
            Assert.False(await db.DeliveryCommandReceipts.AnyAsync(x=>x.ClientRequestId==request.ClientRequestId));
            Assert.False(await db.Set<GaoApp.Domain.Entities.PosOperationReceipt>().AnyAsync(x=>x.OperationId==request.ClientRequestId));
            Assert.Equal(count, await db.Orders.CountAsync()); Assert.Equal(OrderStatus.Draft,(await db.Orders.SingleAsync(x=>x.Id==c.CartId)).Status);
            Assert.Equal(c.CartId,(await db.POSShifts.SingleAsync(x=>x.TerminalId==c.Account.Store.TerminalId && x.Status==POSShiftStatus.Open)).CurrentOrderId);
            Assert.Equal(reserved, await db.InventoryBalances.Select(x=>x.ReservedQty).ToArrayAsync());
        } finally { await fixture.Web.Database.ExecuteAsync($"ALTER TABLE [{table}] DROP CONSTRAINT [{name}];"); }
        var success=await Create(c,request); Assert.True(success.Delivery.Id>0);
    }
    [Theory]
    [InlineData("UPDATE Orders SET Note=N'stale' WHERE Id={0}")]
    [InlineData("UPDATE OrderLines SET Quantity=5 WHERE OrderId={0}")]
    [InlineData("INSERT OrderPayments(StoreId,OrderId,Method,Amount,IsDebtCollection,PaidAtUtc,CreatedAtUtc,IsDeleted) SELECT StoreId,Id,0,1,0,SYSUTCDATETIME(),SYSUTCDATETIME(),0 FROM Orders WHERE Id={0}")]
    public async Task Source_SQL_guards_reject_stale_cart_line_and_payment_writes(string sql)
    {
        using var c = await fixture.CaseAsync(false); await Create(c);
        var ex = await Assert.ThrowsAsync<SqlException>(() => fixture.Web.Database.ExecuteAsync(string.Format(sql,c.CartId)));
        Assert.Equal(51004, ex.Number);
        await using var db=c.Context(); Assert.Equal(OrderStatus.Cancelled,(await db.Orders.SingleAsync(x=>x.Id==c.CartId)).Status);
        Assert.Equal(2,(await db.OrderLines.SingleAsync(x=>x.OrderId==c.CartId)).Quantity); Assert.False(await db.OrderPayments.AnyAsync(x=>x.OrderId==c.CartId));
    }
    [Fact]
    public async Task Source_line_hard_delete_is_rejected_by_delivery_foreign_key()
    {
        using var c=await fixture.CaseAsync(false);await Create(c);
        var ex=await Assert.ThrowsAsync<SqlException>(()=>fixture.Web.Database.ExecuteAsync($"DELETE OrderLines WHERE OrderId={c.CartId}"));
        Assert.Equal(547,ex.Number);Assert.Contains("DeliveryOrderLines",ex.Message);
        await using var db=c.Context();Assert.Equal(2,(await db.OrderLines.SingleAsync(x=>x.OrderId==c.CartId)).Quantity);
    }
    [Fact]
    public async Task Ordinary_POS_cash_sale_still_finalizes_with_source_guards_installed()
    {
        using var c=await fixture.CaseAsync(false);
        await c.Client.JsonAsync(HttpMethod.Post,$"/admin/pos/{c.CartId}/payments",new{clientRequestId=Guid.NewGuid(),amount=40,method=0});
        await c.Client.JsonAsync(HttpMethod.Post,$"/admin/pos/{c.CartId}/finalize");
        await using var db=c.Context(); Assert.Equal(OrderStatus.Completed,(await db.Orders.SingleAsync(x=>x.Id==c.CartId)).Status);
        Assert.False(await db.DeliveryOrders.AnyAsync(x=>x.SourceCartId==c.CartId)); Assert.Equal(40,(await db.OrderPayments.SingleAsync(x=>x.OrderId==c.CartId)).Amount);
    }
    [Fact]
    public async Task Migration_down_drops_only_D03_guards_and_preserves_delivery_POS_and_history_then_upgrade_matches_model()
    {
        using var c=await fixture.CaseAsync(false);var result=await Create(c);await using var db=c.Context();
        var migrator=db.GetService<IMigrator>();var orders=await db.Orders.CountAsync();var deliveries=await db.DeliveryOrders.CountAsync();
        var revisions=await db.DeliveryRevisions.CountAsync();
        try {
            await migrator.MigrateAsync("20261006153000_AddDeliveryFoundation");
            Assert.Equal(0,await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.triggers WHERE name IN ('TR_Orders_DeliverySource','TR_OrderLines_DeliverySource','TR_OrderPayments_DeliverySource')").SingleAsync());
            Assert.Equal(orders,await db.Orders.CountAsync());Assert.Equal(deliveries,await db.DeliveryOrders.CountAsync());Assert.Equal(revisions,await db.DeliveryRevisions.CountAsync());
            Assert.Equal(result.Delivery.LookupToken,(await db.DeliveryOrders.SingleAsync(x=>x.Id==result.Delivery.Id)).LookupToken);
        } finally { await migrator.MigrateAsync(); }
        Assert.Equal(3,await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.triggers WHERE name IN ('TR_Orders_DeliverySource','TR_OrderLines_DeliverySource','TR_OrderPayments_DeliverySource')").SingleAsync());
        var snapshot=db.GetService<IModelRuntimeInitializer>().Initialize(db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model,designTime:true);
        Assert.False(db.GetService<IMigrationsModelDiffer>().HasDifferences(snapshot.GetRelationalModel(),db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model.GetRelationalModel()));
    }
    [Fact]
    public async Task Multi_warehouse_reservations_are_all_released_but_delivery_keeps_original_physical_source()
    {
        using var c=await fixture.CaseAsync(false);await using var db=c.Context();var s=c.Account.Store;
        var owner=await db.Warehouses.Where(x=>x.Id==s.WarehouseId).Select(x=>x.LegalEntityId).SingleAsync();
        var warehouse=new GaoApp.Domain.Entities.Warehouse{StoreId=s.StoreId,LegalEntityId=owner,Code="D03-W2-"+Guid.NewGuid().ToString("N")[..8],Name="Second reserved warehouse",IsActive=true};
        db.Add(warehouse);await db.SaveChangesAsync();
        var original=await db.InventoryBalances.SingleAsync(x=>x.WarehouseId==s.WarehouseId && x.ProductVariantId==s.VariantId);var reserved=original.ReservedQty;original.ReservedQty+=1;
        var extra=new GaoApp.Domain.Entities.InventoryBalance{StoreId=s.StoreId,WarehouseId=warehouse.Id,ProductVariantId=s.VariantId,OnHandQty=100,ReservedQty=1};db.Add(extra);
        var cart=await db.Orders.Include(x=>x.Lines).SingleAsync(x=>x.Id==c.CartId);cart.UseMultiLegalEntity=true;cart.HasReservation=true;cart.ReservedAtUtc=DateTime.UtcNow;
        foreach(var w in new[]{s.WarehouseId,warehouse.Id}) db.InventoryReservations.Add(new(){StoreId=s.StoreId,WarehouseId=w,ProductVariantId=s.VariantId,ReferenceType=InventoryReferenceType.Order,ReferenceId=c.CartId.ToString(),ReferenceLineId=cart.Lines.Single().Id,ReservedQty=1,Status=InventoryReservationStatus.Active});
        await db.SaveChangesAsync();var before=await db.InventoryBalances.OrderBy(x=>x.Id).Select(x=>x.OnHandQty).ToArrayAsync();var result=await Create(c);db.ChangeTracker.Clear();
        Assert.Equal(s.WarehouseId,result.Delivery.SourceWarehouseId);Assert.Equal(owner,result.Delivery.SourceLegalEntityId);
        Assert.Equal(2,await db.InventoryReservations.CountAsync(x=>x.ReferenceId==c.CartId.ToString() && x.Status==InventoryReservationStatus.Released));
        Assert.Equal(reserved,(await db.InventoryBalances.SingleAsync(x=>x.Id==original.Id)).ReservedQty);Assert.Equal(0,(await db.InventoryBalances.SingleAsync(x=>x.Id==extra.Id)).ReservedQty);
        Assert.Equal(before,await db.InventoryBalances.OrderBy(x=>x.Id).Select(x=>x.OnHandQty).ToArrayAsync());
        Assert.False((await db.Orders.SingleAsync(x=>x.Id==c.CartId)).HasReservation);
    }
}
