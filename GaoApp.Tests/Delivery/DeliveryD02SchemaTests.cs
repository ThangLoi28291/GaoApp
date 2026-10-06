using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GaoApp.Tests.Delivery;

[Collection("DeliveryD02"), Trait("Category", "DeliveryD02")]
public sealed class DeliveryD02SchemaTests(DeliveryD02Fixture fixture)
{
    private const string Previous = "20261005101953_AddOperationsReportsAndTreasury";
    [Fact]
    public async Task Fresh_database_has_no_delivery_demo_and_matches_snapshot()
    {
        await using var sql = new InventoryPostingLocalDb(); await sql.MigrateAsync();
        await using var db = sql.CreateHostContext();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(0, await db.Stores.CountAsync());
        Assert.Equal(0, await db.DeliveryOrders.CountAsync());
        Assert.Equal(0, await db.DeliveryOutboxMessages.CountAsync());
        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
        var initialized = db.GetService<IModelRuntimeInitializer>().Initialize(snapshot, designTime: true);
        Assert.False(db.GetService<IMigrationsModelDiffer>().HasDifferences(initialized.GetRelationalModel(),
            db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model.GetRelationalModel()));
        var columns = await db.Database.SqlQueryRaw<Column>("SELECT TABLE_NAME AS TableName, COLUMN_NAME AS ColumnName, CAST(NUMERIC_PRECISION AS int) AS Precision, CAST(NUMERIC_SCALE AS int) AS Scale FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME IN ('DeliveryOrderLines','DeliveryJournalEntries','DeliveryDispatchCostFragments') AND DATA_TYPE='decimal'").ToListAsync();
        Assert.Contains(columns, x => x.TableName == "DeliveryOrderLines" && x.ColumnName == "OrderedQuantity" && x.Precision == 18 && x.Scale == 4);
        Assert.Contains(columns, x => x.ColumnName == "BaseMultiplier" && x.Scale == 6);
        Assert.Contains(columns, x => x.TableName == "DeliveryJournalEntries" && x.ColumnName == "MoneyAmount" && x.Scale == 2);
        Assert.Contains(columns, x => x.TableName == "DeliveryDispatchCostFragments" && x.ColumnName == "UnitCost" && x.Scale == 6);
        Assert.Contains(columns, x => x.TableName == "DeliveryDispatchCostFragments" && x.ColumnName == "CostAmount" && x.Scale == 4);
    }
    [Fact]
    public async Task Upgrade_and_downgrade_preserve_existing_pos_rows_and_amounts()
    {
        await using var sql = new InventoryPostingLocalDb(); await sql.MigrateAsync(Previous);
        var seed = await sql.SeedInventoryCatalogAsync();
        int id;
        await using (var db = sql.CreateTenantContext(seed.StoreId))
        {
            var user = new User { UserName = "d02-upgrade", PasswordHash = "test-only", FullName = "D02 upgrade" };
            var role = new Role { StoreId = seed.StoreId, Code = "D02", Name = "D02" };
            db.UserInStores.Add(new() { StoreId = seed.StoreId, User = user, Role = role });
            var terminal = new POSTerminal { StoreId = seed.StoreId, Code = "D02", Name = "D02" };
            db.POSTerminals.Add(terminal); await db.SaveChangesAsync();
            var shift = new POSShift { StoreId = seed.StoreId, TerminalId = terminal.Id, WarehouseId = seed.WarehouseId, OpenedByUserId = user.Id };
            db.POSShifts.Add(shift); await db.SaveChangesAsync();
            var variant = await db.ProductVariants.SingleAsync(x => x.Id == seed.ProductVariantId);
            var order = new Order { StoreId = seed.StoreId, POSShiftId = shift.Id, Subtotal = 50, GrandTotal = 50,
                Lines = [new OrderLine { StoreId = seed.StoreId, ProductId = variant.ProductId, VariantId = variant.Id,
                    ItemName = "POS cũ", UnitName = "Gói", Quantity = 2.5m, BaseQuantity = 2.5m, Multiplier = 1, UnitPrice = 20, LineTotal = 50 }] };
            db.Orders.Add(order); await db.SaveChangesAsync(); id = order.Id;
        }
        await sql.MigrateAsync();
        await AssertOld(sql, id);
        await using (var db = sql.CreateHostContext()) Assert.Empty(await db.DeliveryOrders.ToListAsync());
        await sql.MigrateAsync(Previous);
        await AssertOld(sql, id);
        await sql.MigrateAsync();
        await AssertOld(sql, id);
    }
    private static async Task AssertOld(InventoryPostingLocalDb sql, int id)
    {
        await using var db = sql.CreateHostContext(); var order = await db.Orders.Include(x => x.Lines).SingleAsync(x => x.Id == id);
        Assert.Equal(50, order.GrandTotal); Assert.Equal(0, order.PaidTotal); Assert.Equal(GaoApp.Domain.Enums.OrderStatus.Draft, order.Status);
        var line = Assert.Single(order.Lines); Assert.Equal("POS cũ", line.ItemName); Assert.Equal(2.5m, line.Quantity); Assert.Equal(50, line.LineTotal);
    }
    [Fact]
    public async Task Composite_FKs_reject_foreign_warehouse_line_and_nonexistent_event_revision()
    {
        using var c = await fixture.CaseAsync();
        using var other = await fixture.CaseAsync(false);
        await using var db = c.Context();
        var source = await db.Orders.SingleAsync(x => x.Id == other.CartId);
        var shift = await db.POSShifts.SingleAsync(x => x.Id == source.POSShiftId);
        var foreignWarehouse = fixture.Web.Stores[1].WarehouseId;
        var warehouseFailure = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO DeliveryOrders (StoreId,Code,LookupToken,State,Revision,SourceWarehouseId,SourceLegalEntityId,SourceCartId,CreatedTerminalId,CreatedShiftId,CreatedByUserId,RecipientName,RecipientPhone,RecipientAddress,QuotedTotal,IsDeleted) VALUES ({c.Account.Store.StoreId},'D02-FORGED','D02-FORGED',0,1,{foreignWarehouse},{c.Detail.SourceLegalEntityId},{other.CartId},{shift.TerminalId},{shift.Id},{other.Account.UserId},'A','090','Address',40,0)"));
        Assert.Equal(547, warehouseFailure.Number);
        var otherLine = await db.OrderLines.Where(x => x.OrderId == other.CartId).Select(x => x.Id).SingleAsync();
        var lineFailure = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO DeliveryOrderLines (StoreId,DeliveryOrderId,SourceCartId,SourceOrderLineId,VariantId,ItemName,UnitName,BaseUnitName,OrderedQuantity,BaseMultiplier,UnitPrice,Gross,LineDiscount,AllocatedOrderDiscount,Net,IsDeleted) SELECT StoreId,DeliveryOrderId,SourceCartId,{otherLine},VariantId,ItemName,UnitName,BaseUnitName,OrderedQuantity,BaseMultiplier,UnitPrice,Gross,LineDiscount,AllocatedOrderDiscount,Net,0 FROM DeliveryOrderLines WHERE DeliveryOrderId={c.Detail.Id}"));
        Assert.Equal(547, lineFailure.Number);
        var eventFailure = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO DeliveryOutboxMessages (StoreId,EventId,DeliveryOrderId,Revision,Action,PayloadJson,IsDeleted) VALUES ({c.Account.Store.StoreId},{Guid.NewGuid()},{c.Detail.Id},999,'forged','{{}}',0)"));
        Assert.Equal(547, eventFailure.Number);
    }
    [Fact]
    public async Task SQL_unique_receipts_and_settlement_identity_enforce_single_posting()
    {
        using var c = await fixture.CaseAsync(); await using var db = c.Context();
        var duplicate = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO DeliveryCommandReceipts (StoreId,ClientRequestId,DeliveryOrderId,ActorUserId,Operation,RequestHash,OutcomeJson,IsDeleted) SELECT StoreId,ClientRequestId,DeliveryOrderId,ActorUserId,Operation,RequestHash,OutcomeJson,0 FROM DeliveryCommandReceipts WHERE DeliveryOrderId={c.Detail.Id}"));
        Assert.Contains(duplicate.Number, new[] { 2601, 2627 });
        // Synthetic journal insert stays inside a rolled-back transaction; it does not post a sale.
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO DeliveryJournalEntries (StoreId,DeliveryOrderId,SourceWarehouseId,SourceLegalEntityId,Kind,PostingKey,BaseQuantity,UnitCost,CostAmount,MoneyAmount,IsDeleted) VALUES ({c.Account.Store.StoreId},{c.Detail.Id},{c.Detail.SourceWarehouseId},{c.Detail.SourceLegalEntityId},6,'D02-SETTLE-1',0,0,0,40,0)");
        var settlement = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO DeliveryJournalEntries (StoreId,DeliveryOrderId,SourceWarehouseId,SourceLegalEntityId,Kind,PostingKey,BaseQuantity,UnitCost,CostAmount,MoneyAmount,IsDeleted) VALUES ({c.Account.Store.StoreId},{c.Detail.Id},{c.Detail.SourceWarehouseId},{c.Detail.SourceLegalEntityId},6,'D02-SETTLE-2',0,0,0,40,0)"));
        Assert.Contains(settlement.Number, new[] { 2601, 2627 });
        await tx.RollbackAsync();
        Assert.False(await db.DeliveryJournalEntries.AnyAsync(x => x.DeliveryOrderId == c.Detail.Id));
    }
    private sealed class Column
    {
        public string TableName { get; set; } = "";
        public string ColumnName { get; set; } = "";
        public int Precision { get; set; }
        public int Scale { get; set; }
    }
}
