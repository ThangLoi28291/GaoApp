using System.Collections;
using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using GaoApp.Application.Common.Exceptions.Pos;
using GaoApp.Application.DTOs.POS;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Migrations;
using GaoApp.Tests.Configuration;
using GaoApp.Tests.Security;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GaoApp.Tests.Delivery;

[Collection("DeliveryD02"), Trait("Category", "DeliveryHeldOrderShift")]
public sealed class DeliveryHeldOrderShiftSqlServerTests(DeliveryD02Fixture fixture)
{
    private const string HeldShift = "20261006170000_PreserveHeldOrderShiftChanges";
    private const string Protection = "20261006163000_ProtectDeliverySourceCarts";
    private const string Foundation = "20261006153000_AddDeliveryFoundation";
    private const string OldSourceFk = "FK_DeliveryOrders_Orders_StoreId_SourceCartId_CreatedShiftId";
    private const string NewSourceFk = "FK_DeliveryOrders_Orders_StoreId_SourceCartId";
    private const string InsertSql = """
        INSERT INTO DeliveryOrders (StoreId,Code,LookupToken,State,Revision,SourceWarehouseId,
            SourceLegalEntityId,SourceCartId,CreatedTerminalId,CreatedShiftId,CreatedByUserId,
            RecipientName,RecipientPhone,RecipientAddress,QuotedTotal,IsDeleted)
        VALUES (@store,@code,@code,0,1,@warehouse,@legal,@cart,@terminal,@shift,@user,
            'guard-race','0901234567','SQL fixture',20,0);
        """;

    [Fact]
    public async Task Real_POS_resume_moves_an_ordinary_held_order_to_an_owned_other_counter_and_retains_its_work()
    {
        using var a = await fixture.CaseAsync(false);
        using var b = await fixture.CaseAsync(false);
        await a.Client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/hold", new { holdNote = "cross-counter held order" });
        await using var db = a.Context();
        var before = await db.Orders.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == a.CartId);
        var shiftB = await db.Orders.Where(x => x.Id == b.CartId).Select(x => x.POSShiftId).SingleAsync();
        Assert.NotEqual(before.POSShiftId, shiftB);
        Assert.Equal(OrderStatus.OnHold, before.Status);
        var reservations = await ReservationSnapshot(db, a.CartId);
        Assert.NotEmpty(reservations);
        var balances = await db.InventoryBalances.OrderBy(x => x.Id).Select(x => x.OnHandQty).ToArrayAsync();
        var effects = await PostingSnapshot(db);
        var list = await b.Client.Http.GetFromJsonAsync<List<HeldOrderDto>>("/admin/pos/orders/held");
        var held = Assert.Single(list!, x => x.OrderId == a.CartId);
        Assert.False(held.IsCurrentShift);
        Assert.False(held.IsCurrentTerminal);

        // This endpoint resolves the production POSService and tracks the real persisted Order.
        var resumed = await b.Client.JsonAsync(HttpMethod.Post, $"/admin/pos/orders/{a.CartId}/resume", new { });
        Assert.Equal(a.CartId, resumed.GetProperty("orderId").GetInt32());
        var after = await db.Orders.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == a.CartId);
        Assert.Equal(OrderStatus.Draft, after.Status);
        Assert.Equal(shiftB, after.POSShiftId);
        Assert.Equal(a.CartId, (await db.POSShifts.AsNoTracking().SingleAsync(x => x.Id == shiftB)).CurrentOrderId);
        Assert.Equal(before.GrandTotal, after.GrandTotal);
        Assert.Equal(before.PaidTotal, after.PaidTotal);
        Assert.Equal(before.HoldCode, after.HoldCode);
        Assert.Equal(before.HoldNote, after.HoldNote);
        Assert.Equal(before.Lines.Select(x => (x.Id, x.Quantity, x.BaseQuantity, x.UnitPrice, x.LineTotal)),
            after.Lines.Select(x => (x.Id, x.Quantity, x.BaseQuantity, x.UnitPrice, x.LineTotal)));
        Assert.Equal(OrderStatus.OnHold, (await db.Orders.AsNoTracking().SingleAsync(x => x.Id == b.CartId)).Status);
        Assert.NotEmpty(await ReservationSnapshot(db, b.CartId));
        Assert.Equal(reservations, await ReservationSnapshot(db, a.CartId));
        Assert.Equal(balances, await db.InventoryBalances.OrderBy(x => x.Id).Select(x => x.OnHandQty).ToArrayAsync());
        Assert.Equal(effects, await PostingSnapshot(db));
        Assert.False(await db.DeliveryOrders.AnyAsync(x => x.SourceCartId == a.CartId || x.SourceCartId == b.CartId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_POS_resume_retains_store_and_shift_ownership_denials(bool foreignStore)
    {
        using var a = await fixture.CaseAsync(false);
        await a.Client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/hold", new { holdNote = "denied resume" });
        var account = foreignStore ? await ForeignCounterAccount() : await fixture.Web.AddAccountAsync(a.Account.Store, "*");
        using var client = await fixture.Web.LoginAsync(account);
        if (foreignStore)
            await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = account.Store.WarehouseId });
        await using var db = a.Context();
        var before = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == a.CartId);
        using var denied = await client.Http.PostAsJsonAsync($"/admin/pos/orders/{a.CartId}/resume", new { });
        Assert.Equal(foreignStore ? HttpStatusCode.BadRequest : HttpStatusCode.Conflict, denied.StatusCode);
        var problem = await denied.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(foreignStore ? PosErrorCodes.CartCurrentNotFound : PosErrorCodes.ShiftOwnedByAnotherUser,
            problem.GetProperty("errorCode").GetString());
        var after = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == a.CartId);
        Assert.Equal(OrderStatus.OnHold, after.Status);
        Assert.Equal(before.POSShiftId, after.POSShiftId);
        Assert.Equal(before.GrandTotal, after.GrandTotal);
        Assert.Equal(before.RowVersion, after.RowVersion);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Linked_Draft_or_Cancelled_source_rejects_SQL_and_tracked_EF_shift_changes(bool cancelledSource, bool cancelledDelivery)
    {
        using var a = await fixture.CaseAsync(false);
        using var b = await fixture.CaseAsync(false);
        var delivery = cancelledSource ? (await DeliveryD03Support.Create(a)).Delivery : await a.CreateAsync(Guid.NewGuid());
        await using var db = a.Context();
        if (cancelledDelivery)
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE DeliveryOrders SET State={(int)DeliveryState.Cancelled} WHERE Id={delivery.Id}");
        var old = await db.Orders.SingleAsync(x => x.Id == a.CartId);
        Assert.Equal(cancelledSource ? OrderStatus.Cancelled : OrderStatus.Draft, old.Status);
        var shiftB = await db.Orders.Where(x => x.Id == b.CartId).Select(x => x.POSShiftId).SingleAsync();
        var effects = await PostingSnapshot(db);
        var raw = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Orders SET POSShiftId={shiftB},Note='must rollback' WHERE StoreId={old.StoreId} AND Id={old.Id}"));
        Assert.Contains(raw.Number, cancelledSource ? new[] { 51004, 51006 } : new[] { 51006 });
        old.POSShiftId = shiftB;
        var ef = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var sql = Assert.IsType<SqlException>(ef.InnerException);
        Assert.Contains(sql.Number, cancelledSource ? new[] { 51004, 51006 } : new[] { 51006 });
        db.ChangeTracker.Clear();
        var unchanged = await db.Orders.SingleAsync(x => x.Id == a.CartId);
        Assert.Equal(delivery.CreatedShiftId, unchanged.POSShiftId);
        Assert.NotEqual("must rollback", unchanged.Note);
        Assert.Equal(effects, await PostingSnapshot(db));
    }

    [Fact]
    public async Task Linked_soft_deleted_Draft_source_still_rejects_a_shift_change()
    {
        using var a = await fixture.CaseAsync();
        using var b = await fixture.CaseAsync(false);
        await using var db = a.Context();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Orders SET IsDeleted=1 WHERE Id={a.CartId}");
        var shiftB = await db.Orders.Where(x => x.Id == b.CartId).Select(x => x.POSShiftId).SingleAsync();
        var rejected = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Orders SET POSShiftId={shiftB} WHERE Id={a.CartId}"));
        Assert.Equal(51006, rejected.Number);
        Assert.Equal(a.Detail.CreatedShiftId, await db.Orders.IgnoreQueryFilters().Where(x => x.Id == a.CartId).Select(x => x.POSShiftId).SingleAsync());
    }

    [Fact]
    public async Task Unlinked_held_Order_shift_is_mutable_but_the_existing_required_POSShift_FK_remains()
    {
        using var a = await fixture.CaseAsync(false);
        using var b = await fixture.CaseAsync(false);
        await a.Client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/hold", new { holdNote = "mutable" });
        await using var db = a.Context();
        var order = await db.Orders.SingleAsync(x => x.Id == a.CartId);
        var shiftB = await db.Orders.Where(x => x.Id == b.CartId).Select(x => x.POSShiftId).SingleAsync();
        order.POSShiftId = shiftB;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(shiftB, await db.Orders.Where(x => x.Id == a.CartId).Select(x => x.POSShiftId).SingleAsync());
        var missing = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Orders SET POSShiftId={int.MaxValue} WHERE Id={a.CartId}"));
        Assert.Equal(547, missing.Number);
        Assert.Contains("FK_Orders_POSShifts_POSShiftId", missing.Message);
        Assert.Equal(shiftB, await db.Orders.Where(x => x.Id == a.CartId).Select(x => x.POSShiftId).SingleAsync());
    }

    [Theory]
    [InlineData("missing-cart", 547)]
    [InlineData("foreign-cart", 547)]
    [InlineData("wrong-shift", 51005)]
    [InlineData("wrong-terminal", 547)]
    public async Task Direct_Delivery_insert_rejects_invalid_source_or_shift_without_posting(string fault, int error)
    {
        using var a = await fixture.CaseAsync(false);
        using var b = await fixture.CaseAsync(false);
        var origin = await ReadOrigin(a);
        var other = await ReadOrigin(b);
        if (fault == "missing-cart") origin = origin with { Cart = int.MaxValue };
        if (fault == "foreign-cart")
        {
            var account = await ForeignCounterAccount();
            using var client = await fixture.Web.LoginAsync(account);
            await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = account.Store.WarehouseId });
            var draft = await client.JsonAsync(HttpMethod.Post, "/admin/pos/draft");
            origin = origin with { Cart = draft.GetProperty("orderId").GetInt32() };
        }
        if (fault == "wrong-shift") origin = origin with { Shift = other.Shift, Terminal = other.Terminal };
        if (fault == "wrong-terminal") origin = origin with { Terminal = other.Terminal };
        await using var db = a.Context();
        var effects = await PostingSnapshot(db);
        var result = await InsertTransaction(origin, Guid.NewGuid().ToString("N"));
        Assert.False(result.Committed);
        Assert.Equal(error, result.Error);
        Assert.False(await db.DeliveryOrders.AnyAsync(x => x.SourceCartId == a.CartId));
        Assert.Equal(effects, await PostingSnapshot(db));
    }

    [Fact]
    public async Task New_Delivery_UPDATE_guard_validates_shift_and_existing_origin_guard_remains_independent()
    {
        using var a = await fixture.CaseAsync();
        using var b = await fixture.CaseAsync(false);
        var other = await ReadOrigin(b);
        await using var db = a.Context();
        var effects = await PostingSnapshot(db);
        var origin = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE DeliveryOrders SET Code='changed-origin' WHERE Id={a.Detail.Id}"));
        Assert.Equal(51003, origin.Number);
        // Temporarily isolate the new UPDATE guard in this owned fixture transaction.
        // Rolling back also restores the original trigger's enabled state.
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlRawAsync("DISABLE TRIGGER [TR_DeliveryOrders_Origin] ON [DeliveryOrders];");
            var shift = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE DeliveryOrders SET CreatedShiftId={other.Shift},CreatedTerminalId={other.Terminal} WHERE Id={a.Detail.Id}"));
            Assert.Equal(51005, shift.Number);
            await tx.RollbackAsync();
        }
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT CAST(is_disabled AS int) AS Value FROM sys.triggers WHERE name='TR_DeliveryOrders_Origin'").SingleAsync());
        Assert.Equal(a.Detail.CreatedShiftId, await db.DeliveryOrders.Where(x => x.Id == a.Detail.Id).Select(x => x.CreatedShiftId).SingleAsync());
        Assert.Equal(effects, await PostingSnapshot(db));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Either_serial_order_has_one_valid_winner_and_rejects_the_other_atomically(bool linkFirst)
    {
        using var a = await fixture.CaseAsync(false);
        using var b = await fixture.CaseAsync(false);
        var origin = await ReadOrigin(a);
        var next = await ReadOrigin(b);
        var code = Guid.NewGuid().ToString("N");
        Attempt link, shift;
        if (linkFirst) { link = await InsertTransaction(origin, code); shift = await ShiftTransaction(origin, next.Shift); }
        else { shift = await ShiftTransaction(origin, next.Shift); link = await InsertTransaction(origin, code); }
        Assert.True(linkFirst ? link.Committed : shift.Committed);
        Assert.Equal(linkFirst ? 51006 : 51005, linkFirst ? shift.Error : link.Error);
        await AssertWinner(a, origin, next.Shift, link, shift);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Contended_link_and_shift_change_never_commit_mismatched_linkage(int iteration)
    {
        using var a = await fixture.CaseAsync(false);
        using var b = await fixture.CaseAsync(false);
        var origin = await ReadOrigin(a);
        var next = await ReadOrigin(b);
        await using var gate = LocalDbSqlConnectionFactory.Create(fixture.Web.Database.ConnectionString);
        await gate.OpenAsync();
        await using var tx = (SqlTransaction)await gate.BeginTransactionAsync();
        await using (var hold = gate.CreateCommand())
        {
            hold.Transaction = tx;
            // Hold both physical keys so neither FK validation nor the Order update can pass the barrier.
            hold.CommandText = """
                SELECT Id FROM Orders WITH (XLOCK,HOLDLOCK,INDEX([PK_Orders])) WHERE StoreId=@store AND Id=@cart;
                SELECT Id FROM Orders WITH (XLOCK,HOLDLOCK,INDEX([AK_Orders_StoreId_Id])) WHERE StoreId=@store AND Id=@cart;
                """;
            hold.Parameters.AddWithValue("@store", origin.Store);
            hold.Parameters.AddWithValue("@cart", origin.Cart);
            Assert.Equal(origin.Cart, Convert.ToInt32(await hold.ExecuteScalarAsync()));
        }
        var linkReady = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var shiftReady = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var link = InsertTransaction(origin, "race-" + iteration + "-" + Guid.NewGuid().ToString("N")[..20], linkReady);
        var shift = ShiftTransaction(origin, next.Shift, shiftReady);
        var sessions = await Task.WhenAll(linkReady.Task, shiftReady.Task).WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var blocked = 0;
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline && blocked < 2)
            {
                await using var inspect = gate.CreateCommand();
                inspect.Transaction = tx;
                inspect.CommandText = "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE session_id IN (@link,@shift) AND blocking_session_id>0";
                inspect.Parameters.AddWithValue("@link", sessions[0]);
                inspect.Parameters.AddWithValue("@shift", sessions[1]);
                blocked = Convert.ToInt32(await inspect.ExecuteScalarAsync());
                if (blocked < 2) await Task.Delay(25);
            }
            Assert.Equal(2, blocked); // Both actual SQL statements must contend before releasing the barrier.
        }
        finally { await tx.RollbackAsync(); }
        var results = await Task.WhenAll(link, shift).WaitAsync(TimeSpan.FromSeconds(40));
        Assert.Single(results, x => x.Committed);
        Assert.Contains(Assert.Single(results, x => !x.Committed).Error, new int?[] { 51005, 51006, 1205 });
        await AssertWinner(a, origin, next.Shift, results[0], results[1]);
    }

    [Theory]
    [InlineData("20261004122812_AddPendingSalesReturnRestock", 64)]
    [InlineData(Foundation, 67)]
    [InlineData(Protection, 68)]
    public async Task Prefix_upgrade_and_safe_Down_preserve_POS_delivery_and_history_records(string prefix, int count)
    {
        await using var sql = new InventoryPostingLocalDb();
        await sql.MigrateAsync(prefix);
        try
        {
            var seed = await SeedLegacy(sql, includeDelivery: count >= 67);
            await using var db = sql.CreateHostContext();
            Assert.Equal(count, (await db.Database.GetAppliedMigrationsAsync()).Count());
            var before = await LegacySnapshot(db);
            await sql.MigrateAsync();
            Assert.Equal(before, await LegacySnapshot(db));
            await AssertSchema(db, latest: true);
            Assert.False(db.Database.HasPendingModelChanges());

            await sql.MigrateAsync(Protection);
            Assert.Equal(before, await LegacySnapshot(db));
            await AssertSchema(db, latest: false);
            await sql.MigrateAsync();
            Assert.Equal(before, await LegacySnapshot(db));
            await AssertSchema(db, latest: true);
            Assert.Equal(2, await db.OrderLines.Where(x => x.OrderId == seed.HeldCart).Select(x => x.Quantity).SingleAsync());
            Assert.Equal(OrderStatus.OnHold, await db.Orders.Where(x => x.Id == seed.HeldCart).Select(x => x.Status).SingleAsync());
        }
        finally { await sql.MigrateAsync(); }
    }

    [Fact]
    public async Task Upgrade_rejects_a_mismatched_source_behind_a_disabled_old_FK_before_any_schema_changes()
    {
        await using var sql = new InventoryPostingLocalDb();
        await sql.MigrateAsync(Protection);
        var seed = await SeedLegacy(sql, includeDelivery: true);
        await using var db = sql.CreateHostContext();
        var valid = await LegacySnapshot(db);
        await db.Database.ExecuteSqlRawAsync($"ALTER TABLE DeliveryOrders NOCHECK CONSTRAINT [{OldSourceFk}];");
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Orders SET POSShiftId={seed.OtherShift} WHERE Id={seed.SourceCart}");
            var corrupt = await LegacySnapshot(db);
            Assert.NotEqual(valid, corrupt);
            var failure = await Assert.ThrowsAsync<SqlException>(() => sql.MigrateAsync());
            Assert.Equal(51005, failure.Number);
            Assert.Equal(corrupt, await LegacySnapshot(db));
            Assert.Equal(Protection, (await db.Database.GetAppliedMigrationsAsync()).Last());
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>($"SELECT COUNT(*) AS Value FROM sys.key_constraints WHERE name='AK_Orders_StoreId_Id_POSShiftId'").SingleAsync());
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>($"SELECT COUNT(*) AS Value FROM sys.foreign_keys WHERE name='{OldSourceFk}' AND is_disabled=1").SingleAsync());
            Assert.Equal(0, await db.Database.SqlQueryRaw<int>($"SELECT COUNT(*) AS Value FROM sys.foreign_keys WHERE name='{NewSourceFk}'").SingleAsync());
            Assert.Equal(10, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.triggers WHERE parent_class=1 AND is_ms_shipped=0").SingleAsync());
        }
        finally
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Orders SET POSShiftId={seed.SourceShift} WHERE Id={seed.SourceCart}");
            await db.Database.ExecuteSqlRawAsync($"ALTER TABLE DeliveryOrders WITH CHECK CHECK CONSTRAINT [{OldSourceFk}];");
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>($"SELECT COUNT(*) AS Value FROM sys.foreign_keys WHERE name='{OldSourceFk}' AND is_disabled=0 AND is_not_trusted=0").SingleAsync());
        }
        Assert.Equal(valid, await LegacySnapshot(db));
        await sql.MigrateAsync();
        await AssertSchema(db, latest: true);
    }

    private static async Task AssertSchema(AppDbContext db, bool latest)
    {
        var ids = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(latest ? 69 : 68, ids.Length);
        Assert.Equal(latest ? HeldShift : Protection, ids[^1]);
        var catalog = new EfCoreDatabaseSchemaManifestCatalog(db);
        Assert.True(catalog.TryGetManifestForAppliedMigrationPrefix(ids, out var expected));
        await db.Database.OpenConnectionAsync();
        var actual = await new SqlServerSchemaSnapshotReader(db).ReadAsync(ids);
        var comparison = DatabaseSchemaComparer.Compare(expected, actual);
        Assert.True(comparison.IsMatch, JsonSerializer.Serialize(comparison.Mismatches));
        Assert.Equal(latest ? 12 : 10, actual.Tables.Sum(x => x.Triggers.Count));
        var orders = actual.Tables.Single(x => x.Identity.Name == "orders");
        var source = Assert.Single(actual.Tables.Single(x => x.Identity.Name == "deliveryorders").ForeignKeys,
            x => x.PrincipalTable.Name == "orders");
        Assert.Equal(latest ? new[] { "storeid", "sourcecartid" } : new[] { "storeid", "sourcecartid", "createdshiftid" }, source.Columns);
        Assert.Equal(latest ? new[] { "storeid", "id" } : new[] { "storeid", "id", "posshiftid" }, source.PrincipalColumns);
        Assert.False(source.IsDisabled || source.IsNotTrusted);
        Assert.Equal(!latest, orders.Indexes.Any(x => x.Name == "ak_orders_storeid_id_posshiftid"));
        Assert.Contains(orders.ForeignKeys, x => x.Columns.SequenceEqual(new[] { "posshiftid" }) && x.PrincipalTable.Name == "posshifts");
        Assert.False(orders.Columns.Single(x => x.Name == "posshiftid").IsNullable);
    }

    private static async Task<LegacySeed> SeedLegacy(InventoryPostingLocalDb sql, bool includeDelivery)
    {
        var catalog = await sql.SeedInventoryCatalogAsync();
        await using var db = sql.CreateTenantContext(catalog.StoreId);
        var user = new User { UserName = "held-key-upgrade", PasswordHash = "fixture-only", FullName = "Held key migration" };
        var role = new Role { StoreId = catalog.StoreId, Code = "HELD", Name = "Held migration" };
        db.UserInStores.Add(new() { StoreId = catalog.StoreId, User = user, Role = role });
        var terminalA = new POSTerminal { StoreId = catalog.StoreId, Code = "HELD-A", Name = "Counter A" };
        var terminalB = new POSTerminal { StoreId = catalog.StoreId, Code = "HELD-B", Name = "Counter B" };
        db.POSTerminals.AddRange(terminalA, terminalB);
        await db.SaveChangesAsync();
        var shiftA = new POSShift { StoreId = catalog.StoreId, TerminalId = terminalA.Id, WarehouseId = catalog.WarehouseId, OpenedByUserId = user.Id };
        var shiftB = new POSShift { StoreId = catalog.StoreId, TerminalId = terminalB.Id, WarehouseId = catalog.WarehouseId, OpenedByUserId = user.Id };
        db.POSShifts.AddRange(shiftA, shiftB);
        await db.SaveChangesAsync();
        var variant = await db.ProductVariants.SingleAsync(x => x.Id == catalog.ProductVariantId);
        // Use the actual historical columns. Current EF Order inserts would emit
        // CustomerPriceTierSnapshot, which does not exist until migration65.
        var heldId = await InsertLegacyCart(sql, catalog.StoreId, shiftA.Id, variant.ProductId, variant.Id, OrderStatus.OnHold, 2);
        var sourceId = await InsertLegacyCart(sql, catalog.StoreId, shiftA.Id, variant.ProductId, variant.Id, OrderStatus.Draft, 1);
        var sourceLineId = await db.OrderLines.Where(x => x.OrderId == sourceId).Select(x => x.Id).SingleAsync();
        if (includeDelivery)
        {
            var legal = await db.Warehouses.Where(x => x.Id == catalog.WarehouseId).Select(x => x.LegalEntityId).SingleAsync();
            var delivery = new DeliveryOrder { StoreId = catalog.StoreId, Code = "OLD-DELIVERY", LookupToken = "OLD-LOOKUP",
                SourceCartId = sourceId, SourceWarehouseId = catalog.WarehouseId, SourceLegalEntityId = legal,
                CreatedShiftId = shiftA.Id, CreatedTerminalId = terminalA.Id, CreatedByUserId = user.Id,
                RecipientName = "Historical recipient", RecipientPhone = "0901234567", RecipientAddress = "Historical address", QuotedTotal = 20,
                Lines = [new() { StoreId = catalog.StoreId, SourceCartId = sourceId, SourceOrderLineId = sourceLineId,
                    VariantId = variant.Id, ItemName = "Historical cart", UnitName = "Pack", BaseUnitName = "Pack", OrderedQuantity = 1,
                    BaseMultiplier = 1, UnitPrice = 20, Gross = 20, Net = 20 }] };
            db.DeliveryOrders.Add(delivery);
            await db.SaveChangesAsync();
            db.DeliveryRevisions.Add(new() { StoreId = catalog.StoreId, DeliveryOrderId = delivery.Id, Revision = 1,
                ActorUserId = user.Id, Action = "Created", AggregateVersion = "fixture", SnapshotJson = "{\"fixture\":true}", SnapshotHash = new string('A', 64) });
            await db.SaveChangesAsync();
        }
        return new(heldId, sourceId, shiftA.Id, shiftB.Id);
    }

    private static async Task<int> InsertLegacyCart(InventoryPostingLocalDb sql, int store, int shift, int product,
        int variant, OrderStatus status, decimal quantity)
    {
        await using var connection = LocalDbSqlConnectionFactory.Create(sql.ConnectionString);
        await connection.OpenAsync();
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT INTO Orders (StoreId,POSShiftId,Status,PaymentStatus,Subtotal,DiscountTotal,OrderDiscount,
                GrandTotal,PaidTotal,BalanceDue,ChangeDue,HasReservation,VoucherDiscountTotal,
                PromotionDiscountTotal,ComboDiscountTotal,HoldCode,HeldAtUtc,CreatedAtUtc,IsDeleted)
            VALUES (@store,@shift,@status,0,@total,0,0,@total,0,@total,0,0,0,0,0,
                CASE WHEN @status=1 THEN 'OLD-HOLD' ELSE NULL END,
                CASE WHEN @status=1 THEN SYSUTCDATETIME() ELSE NULL END,SYSUTCDATETIME(),0);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """;
        command.Parameters.AddWithValue("@store", store);
        command.Parameters.AddWithValue("@shift", shift);
        command.Parameters.AddWithValue("@status", (int)status);
        command.Parameters.AddWithValue("@total", quantity * 20);
        var cart = Convert.ToInt32(await command.ExecuteScalarAsync());
        command.CommandText = """
            INSERT INTO OrderLines (StoreId,OrderId,ProductId,VariantId,ItemName,UnitName,Quantity,
                BaseQuantity,Multiplier,UnitPrice,OriginalUnitPrice,LineDiscount,LineTotal,
                PromotionDiscount,ComboAllocatedDiscount,CreatedAtUtc,IsDeleted)
            VALUES (@store,@cart,@product,@variant,'Historical cart','Pack',@quantity,
                @quantity,1,20,20,0,@total,0,0,SYSUTCDATETIME(),0);
            """;
        command.Parameters.AddWithValue("@cart", cart);
        command.Parameters.AddWithValue("@product", product);
        command.Parameters.AddWithValue("@variant", variant);
        command.Parameters.AddWithValue("@quantity", quantity);
        await command.ExecuteNonQueryAsync();
        await tx.CommitAsync();
        return cart;
    }

    private static async Task<string> LegacySnapshot(AppDbContext db)
    {
        db.ChangeTracker.Clear();
        var hasDelivery = (await db.Database.GetAppliedMigrationsAsync()).Contains(Foundation);
        var deliveries = hasDelivery ? await db.DeliveryOrders.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.SourceCartId, x.CreatedShiftId, x.CreatedTerminalId, x.Code, x.LookupToken, x.QuotedTotal }).ToArrayAsync() : [];
        var history = hasDelivery ? await db.DeliveryRevisions.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.DeliveryOrderId, x.SnapshotHash, x.SnapshotJson }).ToArrayAsync() : [];
        var deliveryLines = hasDelivery ? await db.DeliveryOrderLines.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.SourceCartId, x.SourceOrderLineId, x.OrderedQuantity, x.Net }).ToArrayAsync() : [];
        return JsonSerializer.Serialize(new {
            Orders = await db.Orders.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.StoreId, x.POSShiftId, x.Status, x.Subtotal, x.GrandTotal, x.PaidTotal, x.BalanceDue, x.HoldCode }).ToArrayAsync(),
            Lines = await db.OrderLines.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.OrderId, x.Quantity, x.LineTotal }).ToArrayAsync(),
            Delivery = deliveries,
            DeliveryLines = deliveryLines,
            History = history
        });
    }

    private async Task AssertWinner(DeliveryD02Case a, Origin origin, int nextShift, Attempt link, Attempt shift)
    {
        await using var db = a.Context();
        var order = await db.Orders.SingleAsync(x => x.Id == origin.Cart);
        Assert.Equal(shift.Committed ? nextShift : origin.Shift, order.POSShiftId);
        Assert.Equal(shift.Committed ? "shift-winner" : null, order.Note);
        var deliveries = await db.DeliveryOrders.Where(x => x.SourceCartId == origin.Cart).ToListAsync();
        if (link.Committed) Assert.Equal(order.POSShiftId, Assert.Single(deliveries).CreatedShiftId);
        else Assert.Empty(deliveries);
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM DeliveryOrders g LEFT JOIN Orders o ON o.StoreId=g.StoreId AND o.Id=g.SourceCartId WHERE o.Id IS NULL OR o.POSShiftId<>g.CreatedShiftId").SingleAsync());
        Assert.False(await db.OrderPayments.AnyAsync(x => x.OrderId == origin.Cart));
        Assert.False(await db.DeliveryRevisions.AnyAsync(x => deliveries.Select(d => d.Id).Contains(x.DeliveryOrderId)));
        Assert.False(await db.DeliveryJournalEntries.AnyAsync(x => deliveries.Select(d => d.Id).Contains(x.DeliveryOrderId)));
    }

    private async Task<Origin> ReadOrigin(DeliveryD02Case c)
    {
        await using var db = c.Context();
        var order = await db.Orders.SingleAsync(x => x.Id == c.CartId);
        var shift = await db.POSShifts.SingleAsync(x => x.Id == order.POSShiftId);
        var legal = await db.Warehouses.Where(x => x.Id == shift.WarehouseId).Select(x => x.LegalEntityId).SingleAsync();
        return new(c.Account.Store.StoreId, c.CartId, shift.Id, shift.TerminalId, shift.WarehouseId, legal, c.Account.UserId);
    }

    private async Task<FullApplicationFixture.Account> ForeignCounterAccount()
    {
        var store = fixture.Web.Stores[1];
        await using var db = fixture.Web.Database.CreateTenantContext(store.StoreId);
        var terminal = new POSTerminal { StoreId = store.StoreId, Code = "HELD-" + Guid.NewGuid().ToString("N")[..12], Name = "Held foreign counter" };
        db.POSTerminals.Add(terminal);
        await db.SaveChangesAsync();
        return await fixture.Web.AddAccountAsync(store with { TerminalId = terminal.Id }, "*");
    }

    private Task<Attempt> InsertTransaction(Origin origin, string code, TaskCompletionSource<int>? ready = null)
        => RunTransaction(InsertSql, origin, command => command.Parameters.AddWithValue("@code", code), ready);

    private Task<Attempt> ShiftTransaction(Origin origin, int nextShift, TaskCompletionSource<int>? ready = null)
        => RunTransaction("UPDATE Orders SET POSShiftId=@nextShift,Note='shift-winner' WHERE StoreId=@store AND Id=@cart",
            origin, command => command.Parameters.AddWithValue("@nextShift", nextShift), ready);

    private async Task<Attempt> RunTransaction(string sql, Origin origin, Action<SqlCommand> configure, TaskCompletionSource<int>? ready)
    {
        await using var connection = LocalDbSqlConnectionFactory.Create(fixture.Web.Database.ConnectionString);
        await connection.OpenAsync();
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandTimeout = 30;
        command.CommandText = sql;
        command.Parameters.AddWithValue("@store", origin.Store);
        command.Parameters.AddWithValue("@cart", origin.Cart);
        command.Parameters.AddWithValue("@shift", origin.Shift);
        command.Parameters.AddWithValue("@terminal", origin.Terminal);
        command.Parameters.AddWithValue("@warehouse", origin.Warehouse);
        command.Parameters.AddWithValue("@legal", origin.Legal);
        command.Parameters.AddWithValue("@user", origin.User);
        configure(command);
        ready?.SetResult(connection.ServerProcessId);
        try
        {
            await command.ExecuteNonQueryAsync();
            await tx.CommitAsync();
            return new(true, null);
        }
        catch (SqlException ex)
        {
            // THROW/1205 may already have aborted the transaction. Disposal still owns cleanup.
            if (tx.Connection is not null) await tx.RollbackAsync();
            return new(false, ex.Number);
        }
    }

    private static async Task<string[]> ReservationSnapshot(AppDbContext db, int cart)
        => (await db.InventoryReservations.AsNoTracking().Where(x => x.ReferenceId == cart.ToString() && x.Status == InventoryReservationStatus.Active)
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.WarehouseId, x.ProductVariantId, x.ReferenceLineId, x.ReservedQty, x.Status }).ToArrayAsync())
            .Select(x => JsonSerializer.Serialize(x)).ToArray();

    private static async Task<string> PostingSnapshot(AppDbContext db)
        => JsonSerializer.Serialize(new { Payments = await db.OrderPayments.CountAsync(), Stock = await db.InventoryTransactions.CountAsync(),
            Debt = await db.Set<CustomerDebtReceipt>().CountAsync(), Deposits = await db.Set<CustomerDepositEntry>().CountAsync(),
            Reward = await db.CustomerRewardLedgers.CountAsync(), Invoices = await db.InvoiceHeads.CountAsync(),
            Revisions = await db.DeliveryRevisions.CountAsync(), Journal = await db.DeliveryJournalEntries.CountAsync(),
            Costs = await db.DeliveryDispatchCostFragments.CountAsync(), Commands = await db.DeliveryCommandReceipts.CountAsync(),
            Outbox = await db.DeliveryOutboxMessages.CountAsync() });

    private sealed record Origin(int Store, int Cart, int Shift, int Terminal, int Warehouse, int Legal, int User);
    private sealed record Attempt(bool Committed, int? Error);
    private sealed record LegacySeed(int HeldCart, int SourceCart, int SourceShift, int OtherShift);
}

[Collection("R1FinalDatabasePreflight"), Trait("Category", "DeliveryHeldOrderShift")]
public sealed class DeliveryHeldOrderShiftManifestTests
{
    [Theory]
    [InlineData("missing-table")]
    [InlineData("missing-key")]
    [InlineData("wrong-table")]
    [InlineData("ordinary-index")]
    [InlineData("remaining-fk")]
    [InlineData("invalid-columns")]
    [InlineData("duplicate-name")]
    public void DropUniqueConstraint_rejects_unrecognized_or_still_referenced_constraints(string fault)
    {
        var replay = new ManifestReplay();
        replay.Apply(Table("Orders"));
        replay.Apply(Table("Other"));
        if (fault == "ordinary-index") replay.Apply(new CreateIndexOperation { Table = "Orders", Name = "AK_Target", Columns = ["StoreId", "Id"], IsUnique = true });
        else replay.Apply(new AddUniqueConstraintOperation { Table = "Orders", Name = "AK_Target", Columns = fault == "invalid-columns" ? ["missing"] : ["StoreId", "Id"] });
        if (fault == "duplicate-name") replay.Apply(new CreateIndexOperation { Table = "Orders", Name = "AK_Target", Columns = ["Id"] });
        if (fault == "remaining-fk") replay.Apply(new AddForeignKeyOperation { Table = "Other", Name = "FK_Remaining", Columns = ["StoreId", "Id"], PrincipalTable = "Orders", PrincipalColumns = ["StoreId", "Id"] });
        var before = JsonSerializer.Serialize(replay.Schema("Orders"));
        Assert.Throws<InvalidOperationException>(() => replay.Apply(new DropUniqueConstraintOperation {
            Table = fault == "missing-table" ? "Absent" : fault == "wrong-table" ? "Other" : "Orders",
            Name = fault == "missing-key" ? "AK_Missing" : "AK_Target" }));
        Assert.Equal(before, JsonSerializer.Serialize(replay.Schema("Orders")));
    }

    [Fact]
    public void DropUniqueConstraint_normalizes_names_and_only_removes_an_unreferenced_unique_constraint()
    {
        var replay = new ManifestReplay();
        replay.Apply(Table("Orders"));
        replay.Apply(Table("Other"));
        replay.Apply(new AddUniqueConstraintOperation { Table = "Orders", Name = "AK_Keep", Columns = ["StoreId", "Id"] });
        replay.Apply(new AddUniqueConstraintOperation { Table = "Orders", Name = "AK_Drop", Columns = ["Id"] });
        replay.Apply(new AddForeignKeyOperation { Table = "Other", Name = "FK_Keep", Columns = ["StoreId", "Id"], PrincipalTable = "Orders", PrincipalColumns = ["StoreId", "Id"] });
        replay.Apply(new DropUniqueConstraintOperation { Schema = "DBO", Table = "ORDERS", Name = "AK_DROP" });
        Assert.Equal("ak_keep", Assert.Single(replay.Schema("Orders").Indexes).Name);
        Assert.Single(replay.Schema("Other").ForeignKeys);
        Assert.Throws<InvalidOperationException>(() => replay.Apply(new DropTableOperation { Name = "Orders" }));
    }

    private static CreateTableOperation Table(string name)
    {
        var table = new CreateTableOperation { Name = name };
        table.Columns.Add(new AddColumnOperation { Name = "StoreId", Table = name, ColumnType = "int", ClrType = typeof(int) });
        table.Columns.Add(new AddColumnOperation { Name = "Id", Table = name, ColumnType = "int", ClrType = typeof(int) });
        return table;
    }

    private sealed class ManifestReplay
    {
        private static readonly Type Catalog = typeof(EfCoreDatabaseSchemaManifestCatalog);
        private readonly IDictionary tables = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(
            typeof(DatabaseObjectIdentity), Catalog.GetNestedType("MutableTable", BindingFlags.NonPublic)!))!;
        internal void Apply(MigrationOperation operation)
        {
            try
            {
                Catalog.GetMethod("ApplyOperation", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null,
                    new object[] { "bounded-manifest-test", operation, tables, new Dictionary<DatabaseObjectIdentity, DatabaseSequenceSchema>(), "dbo" });
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); }
        }
        internal DatabaseTableSchema Schema(string name)
        {
            var table = tables[new DatabaseObjectIdentity("dbo", name)]!;
            return (DatabaseTableSchema)table.GetType().GetMethod("ToSchema")!.Invoke(table, null)!;
        }
    }
}
