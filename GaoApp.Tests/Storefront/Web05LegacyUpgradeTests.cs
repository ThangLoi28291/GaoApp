using GaoApp.Domain.Entities;
using System.Text.Json;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Tests.Configuration;
using GaoApp.Web.Services.Acb;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb05"), Trait("Category", "StorefrontWeb05")]
public sealed class Web05LegacyUpgradeTests
{
    private const string Previous = "20261031180000_AddStorefrontCheckout";
    private const string Migration = "20261031210000_AddStorefrontQrBankEvidence";
    [Theory]
    [InlineData(AcbSessionStatus.Creating)][InlineData(AcbSessionStatus.Pending)][InlineData(AcbSessionStatus.Received)][InlineData(AcbSessionStatus.ReviewRequired)]
    public async Task Unresolved_legacy_QR_without_immutable_bank_binding_blocks_upgrade_before_any_DDL_then_resolved_fixture_can_retry(AcbSessionStatus status)
    {
        await using var sql = new InventoryPostingLocalDb(); await sql.MigrateAsync(Previous); var old = await Legacy(sql, status); var before = await Snapshot(sql); var metadata = await Metadata(sql);
        var error = await Assert.ThrowsAsync<SqlException>(() => sql.MigrateAsync()); Assert.True(error.Number >= 51000, "Expected explicit migration preflight, SQL number=" + error.Number);
        Assert.Equal(before, await Snapshot(sql)); Assert.Equal(metadata, await Metadata(sql)); Assert.Equal(0, await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE name=N'BankVerifiedReceipts'"));
        Assert.Equal(0, await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'AcbQrSessions') AND name=N'BankBindingSnapshotJson'"));
        await using (var db = sql.CreateHostContext()) Assert.Equal(Previous, (await db.Database.GetAppliedMigrationsAsync()).Last());
        // Model the documented operator's old-version resolution in this owned
        // fixture. This is not a real provider cancellation or new cancel API.
        await sql.ExecuteAsync($"UPDATE AcbQrSessions SET Status={(int)AcbSessionStatus.Cancelled} WHERE Id={old.Session}; UPDATE PosPaymentQrRequests SET Status={(int)PosPaymentQrStatus.Cancelled} WHERE Id={old.Qr}");
        var resolved = await Snapshot(sql); await sql.MigrateAsync(); Assert.Equal(resolved, await Snapshot(sql));
        await using var check = sql.CreateTenantContext(old.Seed.StoreId); Assert.Empty(await check.AdminMenuItems.ToListAsync()); Assert.Empty(await check.Set<BankVerifiedReceipt>().ToListAsync());
    }

    [Fact]
    public async Task Completed_and_cancelled_legacy_history_and_raw_bank_bytes_are_preserved_without_fabricated_physical_receipt_backfill()
    {
        await using var sql = new InventoryPostingLocalDb(); await sql.MigrateAsync(Previous); var paid = await Legacy(sql, AcbSessionStatus.Completed, paid: true); await Legacy(sql, AcbSessionStatus.Cancelled);
        var before = await Snapshot(sql); await sql.MigrateAsync(); Assert.Equal(before, await Snapshot(sql));
        await using var db = sql.CreateTenantContext(paid.Seed.StoreId); var old = await db.Set<AcbQrSession>().SingleAsync(); Assert.Null(old.BankBindingSnapshotJson);
        Assert.Null((await db.Set<AcbPaymentTransaction>().SingleAsync()).BankVerifiedReceiptId); Assert.Null((await db.OrderPayments.SingleAsync()).BankVerifiedReceiptId);
        Assert.Empty(await db.Set<BankVerifiedReceipt>().ToListAsync()); Assert.Empty(await db.Set<BankReceiptConsumption>().ToListAsync()); Assert.Empty(await db.AdminMenuItems.ToListAsync());
    }

    [Fact]
    public async Task Legacy_committed_external_identity_with_unproven_physical_account_cannot_be_credited_again_by_web_after_current_account_edit()
    {
        await using var sql = new InventoryPostingLocalDb(); await sql.MigrateAsync(Previous); var old = await Legacy(sql, AcbSessionStatus.Completed, paid: true); await sql.MigrateAsync();
        var c = await old.Seed.Web05Async(); var p = await Web05Prepared.CreateAsync(c); p.Bank.Pay(p.Reference, 45_000m, old.External);
        await c.ProcessAsync(p.IntentId, p.Bank); await c.ProcessAsync(p.IntentId, p.Bank);
        await using var db = c.Context(); Assert.Equal(StorefrontPaymentState.Review, (await db.StorefrontOrders.SingleAsync()).PaymentState);
        Assert.Empty(await db.Set<StorefrontPaymentAllocation>().ToListAsync()); Assert.Single(await db.OrderPayments.ToListAsync()); Assert.Equal(45_000m, (await db.OrderPayments.SingleAsync()).Amount);
        Assert.Null((await db.Set<AcbPaymentTransaction>().SingleAsync()).BankVerifiedReceiptId); Assert.Empty(await db.Set<BankReceiptConsumption>().Where(x => x.Channel == BankPaymentChannel.Storefront).ToListAsync());
        await Web05CashFlowTests.SnapshotIsolation(c.Seed); var current = await Web05CashFlowTests.Flow(c.Seed, DateTime.UtcNow.AddHours(7).Date);
        Assert.Equal(0, current.Summary.Single(x => x.Fund == "bank").Inflow); Assert.NotNull(current.BankReceiptReviews); Assert.Contains(current.BankReceiptReviews, x => x.Amount == 45_000m);
        var original = await Web05CashFlowTests.Flow(c.Seed, new DateTime(2026, 9, 8)); Assert.Equal(45_000m, original.Summary.Single(x => x.Fund == "bank").Inflow);
    }

    [Theory]
    [InlineData("session")][InlineData("transaction")][InlineData("both")]
    public async Task Legacy_committed_external_identity_remains_unproven_after_preupgrade_session_or_transaction_tombstone(string tombstone)
    {
        await using var sql = new InventoryPostingLocalDb(); await sql.MigrateAsync(Previous); var old = await Legacy(sql, AcbSessionStatus.Completed, paid: true);
        // Tombstone historical evidence under its original schema, before any
        // new immutable guard exists. Never disable a financial source trigger.
        if (tombstone is "session" or "both") await sql.ExecuteAsync($"UPDATE AcbQrSessions SET IsDeleted=1 WHERE Id={old.Session}");
        if (tombstone is "transaction" or "both") await sql.ExecuteAsync($"UPDATE AcbPaymentTransactions SET IsDeleted=1 WHERE SessionId={old.Session}");
        var history = await Snapshot(sql); await sql.MigrateAsync(); Assert.Equal(history, await Snapshot(sql));
        var c = await old.Seed.Web05Async(); var p = await Web05Prepared.CreateAsync(c); p.Bank.Pay(p.Reference, 45_000m, old.External);
        await c.ProcessAsync(p.IntentId, p.Bank); await c.ProcessAsync(p.IntentId, p.Bank);
        await using var db = c.Context(); var observedOrder = await db.StorefrontOrders.SingleAsync();
        await File.WriteAllTextAsync(Web05Support.Artifact("repros/R18-" + Guid.NewGuid().ToString("N") + ".json"), JsonSerializer.Serialize(new
        {
            Tombstone = tombstone, Database = new SqlConnectionStringBuilder(sql.ConnectionString).InitialCatalog,
            observedOrder.PaymentState, WebAllocations = await db.Set<StorefrontPaymentAllocation>().CountAsync(),
            WebConsumptions = await db.Set<BankReceiptConsumption>().CountAsync(x => x.Channel == BankPaymentChannel.Storefront),
            LegacyPaid = await db.OrderPayments.SumAsync(x => x.Amount), GlobalReceipts = await db.Set<BankVerifiedReceipt>().Select(x => new { x.Amount, x.NeedsReview, x.ReviewCode }).ToListAsync()
        }));
        Assert.Equal(StorefrontPaymentState.Review, observedOrder.PaymentState);
        Assert.Empty(await db.Set<StorefrontPaymentAllocation>().ToListAsync()); Assert.Empty(await db.Set<BankReceiptConsumption>().Where(x => x.Channel == BankPaymentChannel.Storefront).ToListAsync());
        Assert.Equal(45_000m, Assert.Single(await db.OrderPayments.ToListAsync()).Amount);
        var retained = await db.Set<AcbPaymentTransaction>().IgnoreQueryFilters().SingleAsync(x => x.StoreId == c.StoreId); Assert.Equal(old.External, retained.TransactionNumber); Assert.Equal(45_000m, retained.Amount); Assert.Null(retained.BankVerifiedReceiptId);
        Assert.Equal(tombstone is "transaction" or "both", retained.IsDeleted);
        Assert.Equal(tombstone is "session" or "both", (await db.Set<AcbQrSession>().IgnoreQueryFilters().SingleAsync(x => x.Id == old.Session)).IsDeleted);
        Assert.Equal(history, await Snapshot(sql));
        await Web05CashFlowTests.SnapshotIsolation(c.Seed); var report = await Web05CashFlowTests.Flow(c.Seed, c.Web.Clock.Utc.AddHours(7).Date);
        Assert.Equal(0, report.Summary.Single(x => x.Fund == "bank").Inflow); Assert.NotNull(report.BankReceiptReviews); Assert.Contains(report.BankReceiptReviews, x => x.Amount == 45_000m);
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Downgrade_guard_preserves_new_intent_evidence_and_money_before_any_schema_drop(bool money)
    {
        var isolated = new Web01Fixture(); await isolated.InitializeAsync();
        try
        {
            var p = await Web05Prepared.CreateAsync(isolated); if (money) { p.Bank.Pay(p.Reference, 45_000m); await p.Case.ProcessAsync(p.IntentId, p.Bank); }
            var metadata = await Metadata(isolated.Database); var before = await Snapshot(isolated.Database); var webBefore = await p.Case.Web.CountsAsync();
            await using (var proof = p.Case.Context())
            {
                Assert.Contains("20261101000000_AddStorefrontFulfillmentSettlement", await proof.Database.GetAppliedMigrationsAsync());
                var received = await proof.StorefrontOrders.SingleAsync(); Assert.NotNull(received.UseMultiLegalEntitySnapshot); Assert.NotNull(received.LegalEntityModeCapturedAtUtc);
            }
            var error = await Assert.ThrowsAsync<SqlException>(() => isolated.Database.MigrateAsync(Previous)); Assert.Equal(51301, error.Number);
            Assert.Contains(error.Errors.Cast<SqlError>(), x => x.Number == 51301 && x.Message == "Cancellation, bank scan or actual refund proof exists; downgrade would remove financial or custody meaning. Use a forward repair.");
            Assert.Equal(metadata, await Metadata(isolated.Database)); Assert.Equal(before, await Snapshot(isolated.Database)); Assert.Equal(webBefore, await p.Case.Web.CountsAsync());
            await using var db = p.Case.Context(); Assert.Contains(Migration, await db.Database.GetAppliedMigrationsAsync()); Assert.Single(await db.Set<StorefrontPaymentIntent>().ToListAsync());
            Assert.Equal(money ? 1 : 0, await db.Set<StorefrontPaymentAllocation>().CountAsync());
        }
        finally { await isolated.DisposeAsync(); }
    }

    [Fact]
    public async Task Empty_new_payment_schema_can_downgrade_and_upgrade_again_without_creating_store_menu_or_receipt_data()
    {
        await using var sql = new InventoryPostingLocalDb(); await sql.MigrateAsync(); await sql.MigrateAsync(Previous);
        await using (var db = sql.CreateHostContext()) Assert.DoesNotContain(Migration, await db.Database.GetAppliedMigrationsAsync());
        await sql.MigrateAsync(); await using var check = sql.CreateHostContext(); Assert.Contains(Migration, await check.Database.GetAppliedMigrationsAsync()); Assert.Empty(await check.AdminMenuItems.ToListAsync()); Assert.Empty(await check.Stores.ToListAsync()); Assert.Empty(await check.Set<BankVerifiedReceipt>().ToListAsync());
    }

    private static async Task<LegacyCase> Legacy(InventoryPostingLocalDb sql, AcbSessionStatus status, bool paid = false)
    {
        var inventory = await sql.SeedInventoryCatalogAsync(); await using var db = sql.CreateTenantContext(inventory.StoreId);
        var variant = await db.ProductVariants.SingleAsync(x => x.Id == inventory.ProductVariantId); variant.HasInputInvoice = true; var product = await db.Products.SingleAsync(x => x.Id == variant.ProductId);
        var user = new User { UserName = "w05-legacy-" + Guid.NewGuid().ToString("N"), PasswordHash = "synthetic-only", FullName = "Synthetic old cashier" };
        var role = new Role { StoreId = inventory.StoreId, Code = "W05-OLD", Name = "Synthetic old role" }; db.UserInStores.Add(new() { StoreId = inventory.StoreId, User = user, Role = role });
        var bank = new StoreBankAccount { StoreId = inventory.StoreId, BankCode = "ACB", BankName = "Synthetic old bank", AccountNumber = "000LEGACY", AccountName = "Synthetic old owner" };
        var terminal = new POSTerminal { StoreId = inventory.StoreId, Code = "W05-OLD", Name = "Synthetic old terminal" }; db.AddRange(bank, terminal); await db.SaveChangesAsync();
        var shift = new POSShift { StoreId = inventory.StoreId, TerminalId = terminal.Id, OpenedByUserId = user.Id, WarehouseId = inventory.WarehouseId, Status = POSShiftStatus.Open, OpenedAtUtc = new DateTime(2026, 9, 8, 0, 0, 0) }; db.Add(shift); await db.SaveChangesAsync();
        var conversion = new ProductUnitConversion { StoreId = inventory.StoreId, ProductVariantId = variant.Id, UnitId = product.BaseUnitId, Factor = 1, Price = 45_000m, IsBaseUnit = true }; db.Add(conversion); await db.SaveChangesAsync();
        var order = new Order { StoreId = inventory.StoreId, POSShiftId = shift.Id, GrandTotal = 45_000m, Subtotal = 45_000m, PaidTotal = paid ? 45_000m : 0m, BalanceDue = paid ? 0m : 45_000m,
            Status = paid ? OrderStatus.Completed : OrderStatus.Draft, PaymentStatus = paid ? PaymentStatus.Paid : PaymentStatus.Unpaid, CompletedAtUtc = paid ? new DateTime(2026, 9, 8, 2, 0, 0) : null };
        db.Add(order); await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT OrderLines(StoreId,OrderId,ProductId,VariantId,ItemName,Quantity,BaseQuantity,Multiplier,UnitPrice,LineDiscount,LineTotal,OriginalUnitPrice,PromotionDiscount,ComboAllocatedDiscount,CreatedAtUtc,IsDeleted) VALUES({inventory.StoreId},{order.Id},{product.Id},{variant.Id},N'Old immutable item',1,1,1,45000,0,45000,0,0,0,SYSUTCDATETIME(),0)");
        var lineId = await db.Database.SqlQueryRaw<int>("SELECT Id AS [Value] FROM OrderLines WHERE OrderId={0}", order.Id).SingleAsync();
        db.Entry(order).State = EntityState.Detached;
        order.Lines.Add(new() { Id = lineId, StoreId = inventory.StoreId, OrderId = order.Id, ProductId = product.Id, VariantId = variant.Id, Quantity = 1, BaseQuantity = 1, Multiplier = 1, UnitPrice = 45_000m, LineTotal = 45_000m, ItemName = "Old immutable item" });
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Orders SET CreatedAtUtc='2026-09-08T01:00:00' WHERE Id={order.Id}");
        var qr = new PosPaymentQrRequest { StoreId = inventory.StoreId, OrderId = order.Id, BankAccountId = bank.Id, Amount = 45_000m, RequestCode = "OLD-QR", ConfirmMode = BankQrConfirmMode.Callback,
            QrRenderMode = BankQrRenderMode.ProviderApi, Status = paid ? PosPaymentQrStatus.Paid : status == AcbSessionStatus.Cancelled ? PosPaymentQrStatus.Cancelled : PosPaymentQrStatus.Pending, ExpireAtUtc = new DateTime(2026, 9, 8, 1, 31, 0), QrRawText = "old-private-qr-bytes" };
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT PosPaymentQrRequests(StoreId,OrderId,BankAccountId,Amount,Content,QrRenderMode,ConfirmMode,Status,RequestCode,QrRawText,ExpireAtUtc,CreatedAtUtc,IsDeleted) VALUES({inventory.StoreId},{qr.OrderId},{qr.BankAccountId},{qr.Amount},{qr.Content},{(int)qr.QrRenderMode},{(int)qr.ConfirmMode},{(int)qr.Status},{qr.RequestCode},{qr.QrRawText},{qr.ExpireAtUtc},SYSUTCDATETIME(),0)");
        qr.Id = await db.Database.SqlQueryRaw<int>("SELECT Id AS [Value] FROM PosPaymentQrRequests WHERE OrderId={0}", order.Id).SingleAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE PosPaymentQrRequests SET CreatedAtUtc='2026-09-08T01:01:00' WHERE Id={qr.Id}"); var reference = "GA" + qr.Id.ToString("D10"); int? payment = null;
        if (paid)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT OrderPayments(StoreId,OrderId,Method,Amount,IsDebtCollection,ReferenceCode,Provider,PaidAtUtc,CreatedAtUtc,IsDeleted) VALUES({inventory.StoreId},{order.Id},{(int)PaymentMethod.BankTransfer},45000,0,{reference},N'ACB','2026-09-08T02:00:00',SYSUTCDATETIME(),0)");
            payment = await db.Database.SqlQueryRaw<int>("SELECT Id AS [Value] FROM OrderPayments WHERE OrderId={0}", order.Id).SingleAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE PosPaymentQrRequests SET PaymentId={payment} WHERE Id={qr.Id}");
        }
        var fingerprint = AcbPaymentPolicy.Fingerprint(order);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT AcbQrSessions(StoreId,QrRequestId,OrderId,ShiftId,TerminalId,CashierId,ProviderOrderId,TraceNumber,VirtualAccount,CartFingerprint,Amount,Status,PaymentId,LastRetrieveJson,CreatedAtUtc,IsDeleted) VALUES({inventory.StoreId},{qr.Id},{order.Id},{shift.Id},{terminal.Id},{user.Id},{reference},N'old-trace',N'old-VA',{fingerprint},45000,{(int)status},{payment},N'{{\"originalBankBody\":\"preserve-byte-sentinel\"}}','2026-09-08T01:01:00',0)");
        var session = await db.Database.SqlQueryRaw<int>("SELECT Id AS [Value] FROM AcbQrSessions WHERE QrRequestId={0}", qr.Id).SingleAsync(); var external = "OLD-EXT-" + Guid.NewGuid().ToString("N");
        if (paid) await db.Database.ExecuteSqlInterpolatedAsync($"INSERT AcbPaymentTransactions(StoreId,SessionId,TransactionNumber,Status,Amount,Content,PostedAt,CreatedAtUtc,IsDeleted) VALUES({inventory.StoreId},{session},{external},N'COMPLETED',45000,N'preserve old bank content',N'2026-09-08',SYSUTCDATETIME(),0)");
        db.Add(new AcbCallbackReceipt { StoreId = inventory.StoreId, ClientRequestId = Guid.NewGuid().ToString(), Page = 1, TotalPages = 1, PayloadHash = new string('A', 64), PayloadJson = "{\"oldAuthenticatedRaw\":\"preserve-byte-sentinel\"}", NextAttemptAtUtc = DateTime.UtcNow.AddDays(7) }); await db.SaveChangesAsync();
        var legal = (await db.Warehouses.SingleAsync(x => x.Id == inventory.WarehouseId)).LegalEntityId;
        return new(new(sql, inventory.StoreId, inventory.WarehouseId, legal, product.Id, variant.Id, product.BaseUnitId, conversion.Id, bank.Id, user.Id), session, qr.Id, external);
    }
    private static Task<string> Snapshot(InventoryPostingLocalDb sql) => RequiredScalar(sql,"SELECT CONCAT((SELECT StoreId,GrandTotal,PaidTotal,BalanceDue,Status,CompletedAtUtc FROM Orders ORDER BY Id FOR JSON PATH),(SELECT StoreId,OrderId,Method,Amount,ReferenceCode,Provider,PaidAtUtc FROM OrderPayments ORDER BY Id FOR JSON PATH),(SELECT StoreId,RequestCode,Amount,Status,PaymentId,QrRawText FROM PosPaymentQrRequests ORDER BY Id FOR JSON PATH),(SELECT StoreId,ProviderOrderId,TraceNumber,VirtualAccount,Amount,Status,PaymentId,LastRetrieveJson FROM AcbQrSessions ORDER BY Id FOR JSON PATH),(SELECT StoreId,SessionId,TransactionNumber,Status,Amount,Content,PostedAt FROM AcbPaymentTransactions ORDER BY Id FOR JSON PATH),(SELECT StoreId,PayloadHash,PayloadJson FROM AcbCallbackReceipts ORDER BY Id FOR JSON PATH))");
    private static Task<string> Metadata(InventoryPostingLocalDb sql) => RequiredScalar(sql,"SELECT CONCAT((SELECT name FROM sys.tables ORDER BY name FOR JSON PATH),(SELECT OBJECT_NAME(object_id) AS TableName,name,column_id,system_type_id,max_length,precision,scale,is_nullable FROM sys.columns WHERE OBJECTPROPERTY(object_id,'IsUserTable')=1 ORDER BY object_id,column_id FOR JSON PATH),(SELECT OBJECT_NAME(parent_object_id) AS TableName,name,definition,is_disabled,is_not_trusted FROM sys.check_constraints ORDER BY parent_object_id,name FOR JSON PATH),(SELECT OBJECT_NAME(t.parent_id) AS TableName,t.name,t.is_disabled,m.definition FROM sys.triggers t JOIN sys.sql_modules m ON m.object_id=t.object_id ORDER BY t.parent_id,t.name FOR JSON PATH))");
    private static async Task<string> RequiredScalar(InventoryPostingLocalDb sql, string query) => Assert.IsType<string>(await sql.ExecuteScalarAsync<string>(query));
    private sealed record LegacyCase(Web01Case Seed, int Session, int Qr, string External);
}
