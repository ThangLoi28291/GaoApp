using System.Text.Json;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Migrations;
using GaoApp.Tests.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb06"), Trait("Category", "StorefrontWeb06")]
public sealed class Web06SchemaTests
{
    private const string Previous = "20261031210000_AddStorefrontQrBankEvidence";
    private const string Migration = "20261101000000_AddStorefrontFulfillmentSettlement";
    [Fact]
    public async Task Fresh_native_schema_is_exact_trusted_and_unseeded_with_real_19_4_quantity_columns()
    {
        await using var sql = new InventoryPostingLocalDb(); await sql.MigrateAsync(); await using var db = sql.CreateHostContext();
        Assert.Contains(Migration, await db.Database.GetAppliedMigrationsAsync()); Assert.Empty(await db.Database.GetPendingMigrationsAsync()); Assert.False(db.Database.HasPendingModelChanges());
        var expected = new EfCoreDatabaseSchemaManifestCatalog(db).GetCurrentManifest(); await db.Database.OpenConnectionAsync();
        var actual = await new SqlServerSchemaSnapshotReader(db).ReadAsync((await db.Database.GetAppliedMigrationsAsync()).ToArray()); var comparison = DatabaseSchemaComparer.Compare(expected, actual);
        var folder = "schema/" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(Web06Support.Artifact(folder + "/expected.json"), JsonSerializer.Serialize(expected)); await File.WriteAllTextAsync(Web06Support.Artifact(folder + "/live.json"), JsonSerializer.Serialize(actual));
        await File.WriteAllTextAsync(Web06Support.Artifact(folder + "/comparison.json"), JsonSerializer.Serialize(comparison));
        await File.WriteAllTextAsync(Web06Support.Artifact(folder + "/raw-checks.json"), await db.Database.SqlQueryRaw<string>("SELECT (SELECT OBJECT_NAME(parent_object_id) AS TableName,name,definition,is_disabled,is_not_trusted FROM sys.check_constraints ORDER BY parent_object_id,name FOR JSON PATH) AS [Value]").SingleAsync());
        await File.WriteAllTextAsync(Web06Support.Artifact(folder + "/raw-triggers.json"), await db.Database.SqlQueryRaw<string>("SELECT (SELECT OBJECT_NAME(t.parent_id) AS TableName,t.name,t.is_disabled,m.definition FROM sys.triggers t JOIN sys.sql_modules m ON m.object_id=t.object_id WHERE t.parent_class=1 ORDER BY t.parent_id,t.name FOR JSON PATH) AS [Value]").SingleAsync());
        Assert.True(comparison.IsMatch, JsonSerializer.Serialize(comparison.Mismatches));
        Assert.Equal(0, await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.check_constraints WHERE is_disabled=1 OR is_not_trusted=1"));
        Assert.Equal(0, await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled=1 OR is_not_trusted=1"));
        Assert.Equal(0, await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.columns WHERE name=N'StoreId1'"));
        Assert.Equal(2, await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.columns WHERE (object_id=OBJECT_ID(N'InvoiceDetails') AND name=N'Quantity' OR object_id=OBJECT_ID(N'InvoiceHeads') AND name=N'TotalQuantity') AND precision=19 AND scale=4"));
        Assert.Empty(await db.Stores.ToListAsync()); Assert.Empty(await db.AdminMenuItems.ToListAsync()); Assert.Empty(await db.RolePermissions.ToListAsync()); Assert.Empty(await db.Orders.ToListAsync()); Assert.Empty(await db.InventoryTransactions.ToListAsync());
        Assert.Empty(await db.Set<StorefrontSaleBinding>().ToListAsync()); Assert.Empty(await db.Set<StorefrontNotificationAttestation>().ToListAsync()); Assert.Empty(await db.Set<StorefrontServiceRevenue>().ToListAsync());
    }
    [Fact]
    public async Task Empty_native_schema_round_trip_does_not_seed_store_menu_role_or_financial_data()
    {
        await using var sql = new InventoryPostingLocalDb(); await sql.MigrateAsync(); await sql.MigrateAsync(Previous); await sql.MigrateAsync();
        await using var db = sql.CreateHostContext(); Assert.Contains(Migration, await db.Database.GetAppliedMigrationsAsync()); Assert.Empty(await db.Stores.ToListAsync()); Assert.Empty(await db.AdminMenuItems.ToListAsync());
        Assert.Empty(await db.RolePermissions.ToListAsync()); Assert.Empty(await db.OrderPayments.ToListAsync()); Assert.Empty(await db.InventoryTransactions.ToListAsync());
    }
    [Fact]
    public async Task Old_POS_origin_and_prices_shipping_snapshots_survive_upgrade_without_backfilled_native_identity()
    {
        var fixture = new Web01Fixture(); await fixture.InitializeAsync();
        try
        {
            await fixture.Database.MigrateAsync(Previous); var old = await Old(fixture); var before = await History(fixture.Database);
            await fixture.Database.MigrateAsync(); Assert.Equal(before, await History(fixture.Database)); await fixture.Database.MigrateAsync(); Assert.Equal(before, await History(fixture.Database));
            await using var db = old.Case.Context(); var delivery = await db.DeliveryOrders.SingleAsync(); Assert.Equal(DeliverySourceKind.Pos, delivery.SourceKind); Assert.Equal(old.Cart, delivery.SourceCartId);
            Assert.True(delivery.CreatedShiftId > 0); Assert.True(delivery.CreatedTerminalId > 0); Assert.Null(delivery.StorefrontOrderId); Assert.Null(delivery.StorefrontFulfillmentSourceId); Assert.Null(delivery.StorefrontAllowNegativeInventory);
            var web = await db.StorefrontOrders.SingleAsync(); Assert.Null(web.UseMultiLegalEntitySnapshot); Assert.Null(web.LegalEntityModeCapturedAtUtc); Assert.Equal(50m, web.GoodsAmount); Assert.Equal(30m, web.ShippingCharge); Assert.Equal(80m, web.TotalDue);
            Assert.Empty(await db.Set<StorefrontSaleBinding>().ToListAsync()); Assert.Empty(await db.Set<StorefrontFulfillmentWork>().ToListAsync()); Assert.Empty(await db.Set<StorefrontServiceRevenue>().ToListAsync()); Assert.Empty(await db.AdminMenuItems.ToListAsync());
            var large = await db.Database.SqlQueryRaw<decimal>("SELECT CONVERT(decimal(19,4),CONVERT(decimal(18,3),999999999999999.999)) AS [Value]").SingleAsync(); Assert.Equal(999999999999999.999m, large);
        }
        finally { await fixture.DisposeAsync(); }
    }
    [Fact]
    public async Task Preexisting_unproven_settlement_link_blocks_before_any_DDL_or_historical_reconstruction()
    {
        var fixture = new Web01Fixture(); await fixture.InitializeAsync();
        try
        {
            await fixture.Database.MigrateAsync(Previous); var old = await Old(fixture);
            await using (var db = old.Case.Context())
            {
                db.Add(new StorefrontSettlementLink { StoreId = old.Case.StoreId, StorefrontOrderId = old.Web, FulfillmentSourceId = old.Source,
                    SaleOrderId = old.Cart, SettlingShiftId = old.Shift, SettledByUserId = old.Case.UserId, GoodsAmount = 50, AllocationSnapshotJson = "{\"legacy\":\"unproved-link\"}" }); await db.SaveChangesAsync();
            }
            var before = await History(fixture.Database); var metadata = await SchemaStamp(fixture.Database);
            var error = await Assert.ThrowsAsync<SqlException>(() => fixture.Database.MigrateAsync()); Assert.Equal(51200, error.Number);
            Assert.Equal(before, await History(fixture.Database)); Assert.Equal(metadata, await SchemaStamp(fixture.Database));
            await using var check = fixture.Database.CreateHostContext(); Assert.DoesNotContain(Migration, await check.Database.GetAppliedMigrationsAsync());
        }
        finally { await fixture.DisposeAsync(); }
    }
    [Fact]
    public async Task Genuine_pre_native_manual_history_without_original_account_proof_stays_uncertain_after_current_account_changes()
    {
        var fixture = new Web01Fixture(); await fixture.InitializeAsync();
        try
        {
            await fixture.Database.MigrateAsync(Previous); var old = await Old(fixture); var now = DateTime.UtcNow;
            await using (var db = old.Case.Context())
            {
                // Original schema has no account snapshot columns. This is a cancelled
                // historical manual request, not a newly fabricated certified receipt.
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT PosPaymentQrRequests(StoreId,OrderId,BankAccountId,Amount,Content,RequestCode,QrRenderMode,ConfirmMode,Status,ExpireAtUtc,CreatedAtUtc,IsDeleted) VALUES({old.Case.StoreId},{old.Cart},{old.Case.BankAccountId},50000,N'Historical request with no recoverable original account',N'W06-OLD-MANUAL',0,0,4,{now.AddMinutes(10)},{now},0)");
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE StoreBankAccounts SET AccountNumber=N'000CURRENT-CHANGED-W06' WHERE Id={old.Case.BankAccountId}");
            }
            await fixture.Database.MigrateAsync();
            var c = await Web06Case.CreateAsync(fixture, quantity: 1); var native = await Web06CodSourceTests.NativeAsync(c);
            await using var host = await Web06NotificationHost.StartAsync(c);
            var id = await Web06CodSourceTests.Accepted(host, native, Web06CodSourceTests.Upload(50_000m));
            await using var check = c.Context(); var global = await Web06CodSourceTests.Importer(c, check).ImportAuthenticatedNotificationAsync(id);
            var receipt = await check.Set<BankVerifiedReceipt>().SingleAsync(x => x.Id == global);
            Assert.Equal(50_000m, receipt.Amount); Assert.True(receipt.NeedsReview); Assert.NotEmpty(receipt.ProtectedEvidence);
            Assert.Equal(BankReceiptAssetConfidence.TransactionIdentityAmbiguous, receipt.AssetConfidence); Assert.Equal(BankReceiptRoutingProvenance.Unproven, receipt.RoutingProvenance);
            Assert.Empty(await Web06CodSourceTests.Importer(c, check).ListAsync()); Assert.Empty(await check.Set<BankReceiptConsumption>().ToListAsync());
            await using var history = old.Case.Context(); var qr = await history.PosPaymentQrRequests.SingleAsync();
            Assert.Null(qr.AccountIdentitySnapshot); Assert.Null(qr.AccountVersionSnapshot); Assert.Null(qr.PaymentId); Assert.Equal(PosPaymentQrStatus.Cancelled, qr.Status);
            Assert.Empty(await history.Set<BankNotificationWait>().ToListAsync()); Assert.Empty(await history.Set<AcbQrSession>().ToListAsync());
            Assert.Equal("000CURRENT-CHANGED-W06", (await history.StoreBankAccounts.SingleAsync()).AccountNumber);
        }
        finally { await fixture.DisposeAsync(); }
    }
    [Theory]
    [InlineData("accepted-order")] [InlineData("physical")] [InlineData("sale")]
    public async Task Down_refuses_native_snapshot_or_actual_physical_financial_history_without_any_drop(string phase)
    {
        var fixture = new Web01Fixture(); await fixture.InitializeAsync();
        try
        {
            var c = await Web06Case.CreateAsync(fixture);
            if (phase != "accepted-order") { await Web06FinanceSupport.ConfigureInvoiceOwnersAsync(c.Checkout); await Web06FinanceSupport.DeliveredAsync(c); }
            if (phase == "sale")
            {
                await Web06FinanceSupport.CollectAllAsync(c); var terminal = await Web06FinanceSupport.OwnOpenShiftAsync(c); await Web06FinanceSupport.ReceiveCashAsync(c, await Web06FinanceSupport.ActualCashInAsync(c, terminal, 200_000m));
                await using var db = c.Context(); await Web06FinanceSupport.Settlement(c, db, terminal).PostSaleAsync(c.OrderId, await c.RequestAsync<GaoApp.Application.Storefront.StorefrontOperationRequest>());
            }
            // Finish current service setup first, then isolate this migration's refusal boundary.
            // A later empty WEB07 module may legally roll down in its own EF transaction.
            var beforeEmptyRollback = await c.PhysicalSnapshotAsync(); var businessBeforeEmptyRollback = await History(fixture.Database);
            await fixture.Database.MigrateAsync(Migration);
            Assert.Equal(beforeEmptyRollback, await c.PhysicalSnapshotAsync()); Assert.Equal(businessBeforeEmptyRollback, await History(fixture.Database));
            await using (var version = fixture.Database.CreateHostContext())
            {
                var applied = (await version.Database.GetAppliedMigrationsAsync()).ToArray();
                Assert.Contains(Migration, applied); Assert.DoesNotContain(StorefrontCancellationSchemaSqlCatalog.MigrationId, applied);
            }
            var before = await c.PhysicalSnapshotAsync(); var metadata = await SchemaStamp(fixture.Database); var history = await History(fixture.Database);
            var error = await Assert.ThrowsAsync<SqlException>(() => fixture.Database.MigrateAsync(Previous)); Assert.Equal(51201, error.Number);
            Assert.Equal(metadata, await SchemaStamp(fixture.Database)); Assert.Equal(before, await c.PhysicalSnapshotAsync()); Assert.Equal(history, await History(fixture.Database));
            await using var afterVersion = fixture.Database.CreateHostContext(); Assert.Contains(Migration, await afterVersion.Database.GetAppliedMigrationsAsync());
        }
        finally { await fixture.DisposeAsync(); }
    }
    private static async Task<(Web01Case Case, int Cart, int Shift, int Web, int Source)> Old(Web01Fixture fixture)
    {
        var c = await fixture.CaseAsync(); await c.SaveDefaultsAsync(); await using var db = c.Context(); await c.FullService(db).SaveZoneAsync(await c.ZoneAsync());
        var terminal = new POSTerminal { StoreId = c.StoreId, Code = "W06-OLD", Name = "WEB06 actual old POS terminal" }; db.Add(terminal); await db.SaveChangesAsync();
        var shift = new POSShift { StoreId = c.StoreId, TerminalId = terminal.Id, WarehouseId = c.WarehouseId, OpenedByUserId = c.UserId }; db.Add(shift); await db.SaveChangesAsync();
        var cart = new Order { StoreId = c.StoreId, POSShiftId = shift.Id, Subtotal = 50m, GrandTotal = 50m, PaidTotal = 10m, BalanceDue = 40m, Status = OrderStatus.Draft }; db.Add(cart); await db.SaveChangesAsync();
        var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT OrderPayments(StoreId,OrderId,Method,Amount,IsDebtCollection,PaidAtUtc,CreatedAtUtc,IsDeleted) VALUES({c.StoreId},{cart.Id},{(int)PaymentMethod.Cash},10,0,{now},{now},0)");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT DeliveryOrders(StoreId,Code,LookupToken,State,Revision,SourceWarehouseId,SourceLegalEntityId,SourceCartId,CreatedTerminalId,CreatedShiftId,CreatedByUserId,RecipientName,RecipientPhone,RecipientAddress,QuotedTotal,CreatedAtUtc,IsDeleted) VALUES({c.StoreId},N'W06-OLD-DELIVERY',N'W06-OLD-TOKEN',0,1,{c.WarehouseId},{c.LegalEntityId},{cart.Id},{terminal.Id},{shift.Id},{c.UserId},N'Old recipient',N'0901234567',N'Old immutable address',50,{now},0)");
        var rate = await db.StorefrontShippingRates.SingleAsync(); var quote = new StorefrontQuote { StoreId = c.StoreId, Token = Guid.NewGuid(), Revision = 1, SettingsRevision = 1, ShippingRateId = rate.Id,
            Fingerprint = new string('A', 64), ExpiresAtUtc = now.AddHours(1), GoodsAmount = 50, ShippingCharge = 30, TotalDue = 80, SnapshotJson = "{\"legacy\":true}" }; db.Add(quote); await db.SaveChangesAsync();
        var tokenHash = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT StorefrontOrders(StoreId,Code,GuestTokenHash,QuoteId,Revision,Phase,PaymentMethod,PaymentState,DeliveryState,RefundState,DeliveryMethod,RecipientName,RecipientPhone,RecipientAddress,AddressSnapshotJson,GoodsAmount,ShippingCharge,TotalDue,ShippingFeeTreatment,CreatedAtUtc,IsDeleted) VALUES({c.StoreId},N'W06-OLD-WEB',{tokenHash},{quote.Id},1,2,0,0,0,0,0,N'Old web recipient',N'0901234567',N'Old web address',N'{{}}',50,30,80,1,{now},0)");
        var web = await db.Database.SqlQueryRaw<int>("SELECT Id AS [Value] FROM StorefrontOrders WHERE StoreId={0}", c.StoreId).SingleAsync();
        var source = new StorefrontFulfillmentSource { StoreId = c.StoreId, StorefrontOrderId = web, SourceWarehouseId = c.WarehouseId, SourceLegalEntityId = c.LegalEntityId, DispatchPostingKey = "W06-OLD-" + Guid.NewGuid().ToString("N") }; db.Add(source); await db.SaveChangesAsync();
        return (c, cart.Id, shift.Id, web, source.Id);
    }
    private static async Task<string> History(InventoryPostingLocalDb sql)
    {
        await using var db = sql.CreateHostContext(); return await db.Database.SqlQueryRaw<string>("SELECT (SELECT (SELECT Id,StoreId,POSShiftId,Subtotal,GrandTotal,PaidTotal,BalanceDue,Status FROM Orders ORDER BY Id FOR JSON PATH) AS Orders,(SELECT Id,StoreId,OrderId,Method,Amount,PaidAtUtc FROM OrderPayments ORDER BY Id FOR JSON PATH) AS Payments,(SELECT Id,StoreId,Code,SourceCartId,CreatedTerminalId,CreatedShiftId,QuotedTotal,RecipientAddress FROM DeliveryOrders ORDER BY Id FOR JSON PATH) AS Delivery,(SELECT Id,StoreId,Code,GoodsAmount,ShippingCharge,TotalDue,AddressSnapshotJson FROM StorefrontOrders ORDER BY Id FOR JSON PATH) AS Website,(SELECT Id,StoreId,StorefrontOrderId,FulfillmentSourceId,SaleOrderId,GoodsAmount,AllocationSnapshotJson FROM StorefrontSettlementLinks ORDER BY Id FOR JSON PATH) AS Links,(SELECT Id,StoreId,Title,Url,SortOrder FROM AdminMenuItems ORDER BY Id FOR JSON PATH) AS Menus FOR JSON PATH,WITHOUT_ARRAY_WRAPPER) AS [Value]").SingleAsync();
    }
    private static async Task<string> SchemaStamp(InventoryPostingLocalDb sql)
    {
        await using var db = sql.CreateHostContext(); return await db.Database.SqlQueryRaw<string>("SELECT (SELECT o.type,o.name,c.column_id,c.name AS columnName,c.max_length,c.precision,c.scale,c.is_nullable FROM sys.objects o LEFT JOIN sys.columns c ON c.object_id=o.object_id WHERE o.is_ms_shipped=0 ORDER BY o.type,o.name,c.column_id FOR JSON PATH) AS [Value]").SingleAsync();
    }
}
