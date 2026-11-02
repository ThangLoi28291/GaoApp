using System.Text.Json;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data.Migrations;
using GaoApp.Tests.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb04"), Trait("Category", "StorefrontWeb04")]
public sealed class Web04SchemaTests(Web01Fixture fixture)
{
    private const string Previous = "20261030230000_AddStorefrontBaseUnitPublication";
    private const string Migration = "20261031180000_AddStorefrontCheckout";
    [Fact]
    public async Task Fresh_checkout_schema_matches_manifest_without_data_menu_or_role_grant_seed()
    {
        await using var sql = new InventoryPostingLocalDb(); await sql.MigrateAsync(); await using var db = sql.CreateHostContext();
        Assert.Contains(Migration, await db.Database.GetAppliedMigrationsAsync()); Assert.Empty(await db.Database.GetPendingMigrationsAsync()); Assert.False(db.Database.HasPendingModelChanges());
        await db.Database.OpenConnectionAsync(); var expected = new EfCoreDatabaseSchemaManifestCatalog(db).GetCurrentManifest();
        var actual = await new SqlServerSchemaSnapshotReader(db).ReadAsync((await db.Database.GetAppliedMigrationsAsync()).ToArray()); var comparison = DatabaseSchemaComparer.Compare(expected, actual);
        await File.WriteAllTextAsync(Web04Support.Artifact("schema-expected-manifest.json"), JsonSerializer.Serialize(expected));
        await File.WriteAllTextAsync(Web04Support.Artifact("schema-actual-manifest.json"), JsonSerializer.Serialize(actual));
        var definitions = await db.Database.SqlQueryRaw<string>("SELECT (SELECT OBJECT_SCHEMA_NAME(parent_object_id) AS [Schema],OBJECT_NAME(parent_object_id) AS [Table],name AS [Name],definition AS [Definition],is_disabled AS IsDisabled,is_not_trusted AS IsNotTrusted FROM sys.check_constraints WHERE parent_object_id IN(OBJECT_ID('InventoryReservations'),OBJECT_ID('StorefrontOrderLines'),OBJECT_ID('InventoryTransactions')) ORDER BY parent_object_id,name FOR JSON PATH) AS [Value]").SingleAsync();
        await File.WriteAllTextAsync(Web04Support.Artifact("schema-raw-check-constraints.json"), definitions);
        await File.WriteAllTextAsync(Web04Support.Artifact("schema-current-comparison.json"), JsonSerializer.Serialize(comparison)); Assert.True(comparison.IsMatch, JsonSerializer.Serialize(comparison.Mismatches));
        Assert.Equal(0, await db.StorefrontOrders.CountAsync()); Assert.Equal(0, await db.InventoryReservations.CountAsync()); Assert.Equal(0, await db.AdminMenuItems.CountAsync());
        Assert.Equal(0, await db.RolePermissions.CountAsync()); Assert.Equal(0, await db.Stores.CountAsync()); Assert.Single(await db.Permissions.Where(x => x.Code == StorefrontPermissions.ViewOrders).ToListAsync());
    }
    [Fact]
    public async Task Upgrade_preserves_old_order_money_shipping_fee_unit_name_and_legacy_hold_and_defaults_adjustment_to_zero()
    {
        await using var sql = new InventoryPostingLocalDb(); await sql.MigrateAsync(Previous); var seed = await sql.SeedInventoryCatalogAsync(); int orderId;
        await using (var db = sql.CreateTenantContext(seed.StoreId))
        {
            var variant = await db.ProductVariants.SingleAsync(x => x.Id == seed.ProductVariantId); var product = await db.Products.SingleAsync(x => x.Id == variant.ProductId);
            var zone = new StorefrontShippingZone { StoreId = seed.StoreId, Code = "OLD", Name = "Existing source zone", ProvinceCode = "01", MinDeliveryDays = 1, MaxDeliveryDays = 3 };
            db.Add(zone); await db.SaveChangesAsync(); var rate = new StorefrontShippingRate { StoreId = seed.StoreId, ShippingZoneId = zone.Id, BaseFee = 20000 }; db.Add(rate); await db.SaveChangesAsync();
            var quote = new StorefrontQuote { StoreId = seed.StoreId, Token = Guid.NewGuid(), SettingsRevision = 1, ShippingRateId = rate.Id, Fingerprint = new string('A', 64),
                ExpiresAtUtc = DateTime.UtcNow.AddHours(1), GoodsAmount = 100, ShippingCharge = 20000, TotalDue = 20100, SnapshotJson = "{\"legacy\":true}" }; db.Add(quote); await db.SaveChangesAsync();
            var order = new StorefrontOrder { StoreId = seed.StoreId, Code = "W04-OLD", GuestTokenHash = new string('B', 64), QuoteId = quote.Id, GoodsAmount = 100, ShippingCharge = 20000,
                TotalDue = 20100, RecipientName = "Old recipient", RecipientPhone = "0901234567", RecipientAddress = "Old address", AddressSnapshotJson = "{\"legacy\":true}" };
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT StorefrontOrders(StoreId,Code,GuestTokenHash,QuoteId,Revision,Phase,PaymentMethod,PaymentState,DeliveryState,RefundState,DeliveryMethod,RecipientName,RecipientPhone,RecipientAddress,Note,AddressSnapshotJson,GoodsAmount,ShippingCharge,TotalDue,ShippingFeeTreatment,PaymentExpiresAtUtc,CreatedAtUtc,IsDeleted) VALUES({seed.StoreId},{order.Code},{order.GuestTokenHash},{order.QuoteId},{order.Revision},{(int)order.Phase},{(int)order.PaymentMethod},{(int)order.PaymentState},{(int)order.DeliveryState},{(int)order.RefundState},{(int)order.DeliveryMethod},{order.RecipientName},{order.RecipientPhone},{order.RecipientAddress},{order.Note},{order.AddressSnapshotJson},{order.GoodsAmount},{order.ShippingCharge},{order.TotalDue},{(int)order.ShippingFeeTreatment},{order.PaymentExpiresAtUtc},SYSUTCDATETIME(),0)");
            orderId = await db.Database.SqlQueryRaw<int>("SELECT Id AS [Value] FROM StorefrontOrders WHERE StoreId={0} AND QuoteId={1}", seed.StoreId, quote.Id).SingleAsync(); order.Id = orderId;
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT StorefrontOrderLines(StoreId,StorefrontOrderId,ProductId,VariantId,SellingUnitId,ItemName,UnitName,Quantity,BaseMultiplier,BaseQuantity,UnitPrice,Discount,NetAmount,WeightKgPerSellingUnit,IsGift,SnapshotJson,CreatedAtUtc,IsDeleted) VALUES({seed.StoreId},{order.Id},{product.Id},{variant.Id},{product.BaseUnitId},N'Legacy snapshot',N'Legacy selling unit',1,1,1,100,0,100,0,0,N'{{}}',SYSUTCDATETIME(),0)");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT InventoryReservations(StoreId,WarehouseId,ProductVariantId,ReferenceType,ReferenceId,ReservedQty,Status,ReservedAtUtc,CreatedAtUtc,IsDeleted) VALUES({seed.StoreId},{seed.WarehouseId},{variant.Id},1,N'OLD-HOLD',2,1,SYSUTCDATETIME(),SYSUTCDATETIME(),0)");
        }
        var before = await sql.ExecuteScalarAsync<string>("SELECT (SELECT Code,GuestTokenHash,GoodsAmount,ShippingCharge,TotalDue,ShippingFeeTreatment,RecipientName,RecipientAddress,AddressSnapshotJson FROM StorefrontOrders FOR JSON PATH) + (SELECT ReferenceType,ReferenceId,ReservedQty,Status,WarehouseId,ProductVariantId FROM InventoryReservations FOR JSON PATH)");
        await sql.MigrateAsync(); var after = await sql.ExecuteScalarAsync<string>("SELECT (SELECT Code,GuestTokenHash,GoodsAmount,ShippingCharge,TotalDue,ShippingFeeTreatment,RecipientName,RecipientAddress,AddressSnapshotJson FROM StorefrontOrders FOR JSON PATH) + (SELECT ReferenceType,ReferenceId,ReservedQty,Status,WarehouseId,ProductVariantId FROM InventoryReservations FOR JSON PATH)"); Assert.Equal(before, after);
        await using (var verify = sql.CreateTenantContext(seed.StoreId))
        {
            var line = await verify.Set<StorefrontOrderLine>().SingleAsync(); Assert.Equal(orderId, line.StorefrontOrderId); Assert.Equal(0, line.RoundingAdjustment); Assert.Equal("Legacy selling unit", line.UnitName);
            var preserved = await verify.StorefrontOrders.SingleAsync(); Assert.Equal(StorefrontShippingFeeTreatment.StoreServiceFee, preserved.ShippingFeeTreatment);
            Assert.Null(preserved.UseMultiLegalEntitySnapshot); Assert.Null(preserved.LegalEntityModeCapturedAtUtc);
            var hold = await verify.InventoryReservations.SingleAsync(); Assert.Null(hold.StorefrontOrderId); Assert.Null(hold.StorefrontOrderLineId); Assert.Null(hold.StorefrontFulfillmentSourceId);
        }
        var error = await Assert.ThrowsAsync<SqlException>(() => sql.MigrateAsync(Previous)); Assert.Equal(51101, error.Number);
        await using var final = sql.CreateHostContext(); Assert.Contains(Migration, await final.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1, await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM StorefrontOrders"));
        var afterGuard = await sql.ExecuteScalarAsync<string>("SELECT (SELECT Code,GuestTokenHash,GoodsAmount,ShippingCharge,TotalDue,ShippingFeeTreatment,RecipientName,RecipientAddress,AddressSnapshotJson FROM StorefrontOrders FOR JSON PATH) + (SELECT ReferenceType,ReferenceId,ReservedQty,Status,WarehouseId,ProductVariantId FROM InventoryReservations FOR JSON PATH)"); Assert.Equal(before, afterGuard);
        Assert.Equal("0901234567", await sql.ExecuteScalarAsync<string>("SELECT RecipientPhone FROM StorefrontOrders")); Assert.Equal(1, await sql.ExecuteScalarAsync<int>("SELECT Revision FROM StorefrontOrders"));
        Assert.Equal("Legacy selling unit", await sql.ExecuteScalarAsync<string>("SELECT UnitName FROM StorefrontOrderLines")); Assert.Equal(0m, await sql.ExecuteScalarAsync<decimal>("SELECT RoundingAdjustment FROM StorefrontOrderLines"));
    }
    [Fact]
    public async Task Empty_checkout_schema_can_downgrade_and_reapply_without_changing_catalog_or_stock()
    {
        await using var sql = new InventoryPostingLocalDb(); await sql.MigrateAsync(); var seed = await sql.SeedInventoryCatalogAsync();
        var before = await sql.ExecuteScalarAsync<string>("SELECT (SELECT Id,StoreId,Name,BasePrice FROM Products FOR JSON PATH)+(SELECT Id,StoreId,Price FROM ProductVariant FOR JSON PATH)+(SELECT Id,StoreId,OnHandQty,ReservedQty FROM InventoryBalances FOR JSON PATH)");
        await sql.MigrateAsync(Previous); Assert.Equal(0, await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('InventoryReservations') AND name LIKE 'Storefront%';"));
        await sql.MigrateAsync(); var after = await sql.ExecuteScalarAsync<string>("SELECT (SELECT Id,StoreId,Name,BasePrice FROM Products FOR JSON PATH)+(SELECT Id,StoreId,Price FROM ProductVariant FOR JSON PATH)+(SELECT Id,StoreId,OnHandQty,ReservedQty FROM InventoryBalances FOR JSON PATH)"); Assert.Equal(before, after);
        await using var db = sql.CreateTenantContext(seed.StoreId); Assert.Empty(await db.AdminMenuItems.ToListAsync());
    }
    [Theory]
    [InlineData("recipient")][InlineData("orderTotal")][InlineData("orderDelete")][InlineData("linePrice")][InlineData("lineRounding")]
    [InlineData("holdQuantity")][InlineData("holdDelete")][InlineData("holdReference")][InlineData("linkDelete")][InlineData("receiptDelete")][InlineData("eventDelete")][InlineData("outboxPayload")]
    public async Task SQL_rejects_rewriting_received_order_money_source_reservation_and_idempotency_evidence(string target)
    {
        var c = await fixture.Web04Async(); await c.PlaceAsync(Web04Case.PlaceRequest(await c.QuoteAsync())); var before = await c.CountsAsync(); await using var db = c.Context();
        var sql = target switch {
            "recipient" => "UPDATE StorefrontOrders SET RecipientName=N'Forged' WHERE StoreId={0}", "orderTotal" => "UPDATE StorefrontOrders SET GoodsAmount=1,TotalDue=ShippingCharge+1 WHERE StoreId={0}",
            "orderDelete" => "DELETE StorefrontOrders WHERE StoreId={0}", "linePrice" => "UPDATE StorefrontOrderLines SET UnitPrice=1,NetAmount=Quantity WHERE StoreId={0}",
            "lineRounding" => "UPDATE StorefrontOrderLines SET RoundingAdjustment=1,NetAmount=NetAmount+1 WHERE StoreId={0}", "holdQuantity" => "UPDATE InventoryReservations SET ReservedQty=2 WHERE StoreId={0}",
            "holdDelete" => "DELETE InventoryReservations WHERE StoreId={0}", "holdReference" => "UPDATE InventoryReservations SET ReferenceId=N'0001' WHERE StoreId={0}",
            "linkDelete" => "DELETE StorefrontReservationLinks WHERE StoreId={0}", "receiptDelete" => "DELETE StorefrontCommandReceipts WHERE StoreId={0}",
            "eventDelete" => "DELETE StorefrontEvents WHERE StoreId={0}", _ => "UPDATE StorefrontOutboxMessages SET PayloadJson=N'{{}}' WHERE StoreId={0}" };
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql, c.StoreId)); Assert.Contains(error.Number, new[] { 547, 51090, 51102, 51103, 51104 });
        Assert.Equal(before, await c.CountsAsync());
    }
}
