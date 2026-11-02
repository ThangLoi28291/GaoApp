using System.Text.Json;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Migrations;
using GaoApp.Infrastructure.Services.Banking;
using GaoApp.Tests.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb07"), Trait("Category", "StorefrontWeb07")]
public sealed class Web07MigrationTests
{
    private const string Previous = "20261101010000_AddDeliverySalePricePermission";
    private const string PreNative = "20261031210000_AddStorefrontQrBankEvidence";
    private const string Current = "20261101120000_AddStorefrontCancellationRefunds";

    [Fact]
    public void Actual_migration_operations_put_up_and_down_guards_before_every_DDL_and_do_not_create_menus()
    {
        var migration = new AddStorefrontCancellationRefunds();
        var up = Assert.IsType<SqlOperation>(migration.UpOperations[0]); var down = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.StartsWith("IF ", up.Sql.TrimStart()); Assert.Contains("THROW 51300", up.Sql, StringComparison.Ordinal);
        Assert.StartsWith("IF ", down.Sql.TrimStart()); Assert.Contains("THROW 51301", down.Sql, StringComparison.Ordinal);
        foreach (var first in new[] { up, down }) { Assert.DoesNotContain("DROP ", first.Sql, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("ALTER ", first.Sql, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("CREATE ", first.Sql, StringComparison.OrdinalIgnoreCase); }
        foreach (var operation in migration.UpOperations.OfType<SqlOperation>()) Assert.DoesNotContain("INSERT AdminMenuItems", operation.Sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Empty_upgrade_down_reapply_is_trusted_and_does_not_seed_store_role_menu_cancellation_or_actual_money()
    {
        await using var sql = new InventoryPostingLocalDb(); await sql.MigrateAsync(); await sql.MigrateAsync(Previous); await sql.MigrateAsync();
        await using var db = sql.CreateHostContext(); Assert.Contains(Current, await db.Database.GetAppliedMigrationsAsync()); Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges()); Assert.Empty(await db.Stores.ToListAsync()); Assert.Empty(await db.AdminMenuItems.ToListAsync()); Assert.Empty(await db.RolePermissions.ToListAsync());
        Assert.Empty(await db.Set<StorefrontCancellationRequest>().ToListAsync()); Assert.Empty(await db.Set<StorefrontRefundFunding>().ToListAsync()); Assert.Empty(await db.Set<StorefrontRefundEvidence>().ToListAsync());
        Assert.Empty(await db.Set<BankVerifiedReceipt>().ToListAsync()); Assert.Empty(await db.POSShiftCashTransactions.ToListAsync()); Assert.Empty(await db.TreasuryEntries.ToListAsync());
        Assert.Equal(0, await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.check_constraints WHERE is_disabled=1 OR is_not_trusted=1"));
        Assert.Equal(0, await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled=1 OR is_not_trusted=1"));
    }

    [Fact]
    public async Task Genuine_versioned_pre_native_POS_and_unclassified_web_history_survive_current_upgrade_without_fabricated_cancellation_or_payout_proof()
    {
        // Explicit historical arrangement, not a claim of new public placement,
        // actual handover, financial completion, or certified historical payout.
        var isolated = new Web01Fixture();
        try
        {
            await isolated.InitializeAsync(); await isolated.Database.MigrateAsync(PreNative);
            var old = await HistoricalAsync(isolated); await isolated.Database.MigrateAsync(Previous);
            var before = await HistoryAsync(isolated.Database); await isolated.Database.MigrateAsync();
            Assert.Equal(before, await HistoryAsync(isolated.Database)); await isolated.Database.MigrateAsync(); Assert.Equal(before, await HistoryAsync(isolated.Database));
            await using var db = old.Context(); var cart = await db.Orders.SingleAsync(); Assert.Equal(50m, cart.GrandTotal); Assert.Equal(10m, cart.PaidTotal); Assert.Equal(40m, cart.BalanceDue);
            Assert.Equal(10m, (await db.OrderPayments.SingleAsync()).Amount); Assert.Null((await db.OrderPayments.SingleAsync()).BankVerifiedReceiptId);
            Assert.Null((await db.StorefrontOrders.SingleAsync()).UseMultiLegalEntitySnapshot); Assert.Null((await db.StorefrontOrders.SingleAsync()).LegalEntityModeCapturedAtUtc);
            Assert.Empty(await db.Set<StorefrontCancellationRequest>().ToListAsync()); Assert.Empty(await db.Set<StorefrontCancellationDecision>().ToListAsync());
            Assert.Empty(await db.Set<StorefrontRefundFunding>().ToListAsync()); Assert.Empty(await db.Set<StorefrontQrClosureProof>().ToListAsync());
            Assert.Empty(await db.Set<StorefrontBankScanAttempt>().ToListAsync()); Assert.Empty(await db.Set<StorefrontBankScanCompletion>().ToListAsync()); Assert.Empty(await db.AdminMenuItems.ToListAsync());
        }
        finally { await isolated.DisposeAsync(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Current_cancellation_or_actual_payout_evidence_blocks_downgrade_before_any_schema_or_data_loss(bool paid)
    {
        var isolated = new Web01Fixture();
        try
        {
            await isolated.InitializeAsync(); var c = await Web07Case.CreatePhysicalAsync(isolated, prepaidQr: paid);
            await using (var db = c.Context()) await Web07Support.Cancellation(c, db, true).CancelGuestAsync(await c.CancelRequestAsync());
            if (paid)
            {
                int refund; await using (var db = c.Context()) refund = (await db.Set<StorefrontRefund>().SingleAsync()).Id;
                await using (var db = c.Context()) await Web07Support.Refunds(c, db).ApproveAsync(c.OrderId, await c.RefundDecisionAsync(refund, "WEB07 nguồn thật trước hạ phiên bản"));
                var debit = await Web07RecordedDebit.BankAsync(c, 230_000m);
                await using var db2 = c.Context(); await Web07Support.Refunds(c, db2).ConfirmExecutionAsync(c.OrderId,
                    await c.ExecutionRequestAsync(refund, debit.Source, c.Seed.LegalEntityId, c.Seed.BankAccountId, debit.ExternalReference, debit.ActualExecutionAt));
            }
            var schema = await SchemaAsync(isolated.Database); var before = await c.SnapshotAsync();
            var error = await Assert.ThrowsAsync<SqlException>(() => isolated.Database.MigrateAsync(Previous)); Assert.Equal(51301, error.Number);
            Assert.Contains(error.Errors.Cast<SqlError>(), x => x.Number == 51301 && x.Message == "Cancellation, bank scan or actual refund proof exists; downgrade would remove financial or custody meaning. Use a forward repair.");
            Assert.Equal(schema, await SchemaAsync(isolated.Database)); Assert.Equal(before, await c.SnapshotAsync());
            await using var check = c.Context(); Assert.Contains(Current, await check.Database.GetAppliedMigrationsAsync());
            Assert.Single(await check.Set<StorefrontCancellationRequest>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
            Assert.Equal(paid ? 230_000m : 0m, await check.Set<StorefrontRefundEvidence>().Where(x => x.StorefrontOrderId == c.OrderId).SumAsync(x => x.Amount));
        }
        finally { await isolated.DisposeAsync(); }
    }

    // The same old-column boundary independently exercised by the WEB06
    // migration fixture: real owned catalog/cashier/shift, explicit old rows.
    private static async Task<Web01Case> HistoricalAsync(Web01Fixture fixture)
    {
        var c = await fixture.CaseAsync(); await c.SaveDefaultsAsync(); await using var db = c.Context(); await c.FullService(db).SaveZoneAsync(await c.ZoneAsync());
        var terminal = new POSTerminal { StoreId = c.StoreId, Code = "W07-OLD", Name = "WEB07 real historical principal" }; db.Add(terminal); await db.SaveChangesAsync();
        var shift = new POSShift { StoreId = c.StoreId, TerminalId = terminal.Id, WarehouseId = c.WarehouseId, OpenedByUserId = c.UserId }; db.Add(shift); await db.SaveChangesAsync();
        var cart = new Order { StoreId = c.StoreId, POSShiftId = shift.Id, Subtotal = 50m, GrandTotal = 50m, PaidTotal = 10m, BalanceDue = 40m, Status = OrderStatus.Draft };
        db.Add(cart); await db.SaveChangesAsync(); var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT OrderPayments(StoreId,OrderId,Method,Amount,IsDebtCollection,PaidAtUtc,CreatedAtUtc,IsDeleted) VALUES({c.StoreId},{cart.Id},{(int)PaymentMethod.Cash},10,0,{now},{now},0)");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT DeliveryOrders(StoreId,Code,LookupToken,State,Revision,SourceWarehouseId,SourceLegalEntityId,SourceCartId,CreatedTerminalId,CreatedShiftId,CreatedByUserId,RecipientName,RecipientPhone,RecipientAddress,QuotedTotal,CreatedAtUtc,IsDeleted) VALUES({c.StoreId},N'W07-OLD-DELIVERY',N'W07-OLD-TOKEN',0,1,{c.WarehouseId},{c.LegalEntityId},{cart.Id},{terminal.Id},{shift.Id},{c.UserId},N'Old recipient',N'0901234567',N'Old immutable address',50,{now},0)");
        var rate = await db.StorefrontShippingRates.SingleAsync(); var quote = new StorefrontQuote { StoreId = c.StoreId, Token = Guid.NewGuid(), Revision = 1, SettingsRevision = 1, ShippingRateId = rate.Id,
            Fingerprint = new string('A', 64), ExpiresAtUtc = now.AddHours(1), GoodsAmount = 50, ShippingCharge = 30, TotalDue = 80, SnapshotJson = "{\"legacy\":true}" };
        db.Add(quote); await db.SaveChangesAsync(); var tokenHash = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT StorefrontOrders(StoreId,Code,GuestTokenHash,QuoteId,Revision,Phase,PaymentMethod,PaymentState,DeliveryState,RefundState,DeliveryMethod,RecipientName,RecipientPhone,RecipientAddress,AddressSnapshotJson,GoodsAmount,ShippingCharge,TotalDue,ShippingFeeTreatment,CreatedAtUtc,IsDeleted) VALUES({c.StoreId},N'W07-OLD-WEB',{tokenHash},{quote.Id},1,2,0,0,0,0,0,N'Old web recipient',N'0901234567',N'Old web address',N'{{}}',50,30,80,1,{now},0)");
        var web = await db.Database.SqlQueryRaw<int>("SELECT Id AS [Value] FROM StorefrontOrders WHERE StoreId={0}", c.StoreId).SingleAsync();
        db.Add(new StorefrontFulfillmentSource { StoreId = c.StoreId, StorefrontOrderId = web, SourceWarehouseId = c.WarehouseId, SourceLegalEntityId = c.LegalEntityId, DispatchPostingKey = "W07-OLD-" + Guid.NewGuid().ToString("N") });
        await db.SaveChangesAsync(); return c;
    }
    private static async Task<string> HistoryAsync(InventoryPostingLocalDb sql)
    {
        await using var db = sql.CreateHostContext(); return await db.Database.SqlQueryRaw<string>("SELECT (SELECT (SELECT Id,StoreId,POSShiftId,Subtotal,GrandTotal,PaidTotal,BalanceDue,Status FROM Orders ORDER BY Id FOR JSON PATH) AS Orders,(SELECT Id,StoreId,OrderId,Method,Amount,PaidAtUtc FROM OrderPayments ORDER BY Id FOR JSON PATH) AS Payments,(SELECT Id,StoreId,Code,SourceCartId,CreatedTerminalId,CreatedShiftId,QuotedTotal,RecipientAddress FROM DeliveryOrders ORDER BY Id FOR JSON PATH) AS Delivery,(SELECT Id,StoreId,Code,GoodsAmount,ShippingCharge,TotalDue,AddressSnapshotJson FROM StorefrontOrders ORDER BY Id FOR JSON PATH) AS Website,(SELECT Id,StoreId,Title,Url,SortOrder FROM AdminMenuItems ORDER BY Id FOR JSON PATH) AS Menus FOR JSON PATH,WITHOUT_ARRAY_WRAPPER) AS [Value]").SingleAsync();
    }
    private static async Task<string> SchemaAsync(InventoryPostingLocalDb sql)
    {
        await using var db = sql.CreateHostContext(); return await db.Database.SqlQueryRaw<string>("SELECT (SELECT o.type,o.name,c.column_id,c.name AS columnName,c.max_length,c.precision,c.scale,c.is_nullable FROM sys.objects o LEFT JOIN sys.columns c ON c.object_id=o.object_id WHERE o.is_ms_shipped=0 ORDER BY o.type,o.name,c.column_id FOR JSON PATH) AS [Value]").SingleAsync();
    }
}
