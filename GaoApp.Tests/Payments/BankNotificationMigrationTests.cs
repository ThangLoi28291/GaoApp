using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Migrations;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Configuration;
using GaoApp.Web.Services.Acb;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    [Fact]
    public async Task Notification_current_schema_manifest_can_describe_the_deployed_migration()
    {
        await using var db = new PreflightAcceptanceDatabase().CreateContext();
        var manifest = new EfCoreDatabaseSchemaManifestCatalog(db).GetCurrentManifest();
        Assert.Equal(4, manifest.Tables.Count(t => t.Identity.Name.StartsWith("banknotification", StringComparison.Ordinal)));
        var receipts = manifest.Tables.Single(t => t.Identity == new DatabaseObjectIdentity("dbo", "BankNotificationReceipts"));
        Assert.Contains(receipts.Indexes, i => i.IsUnique && i.KeyColumns.Select(c => c.Name).SequenceEqual(new[] { "storeid", "evidencekey" }));
        Assert.Equal(5, receipts.ForeignKeys.Count);
    }

    [Fact]
    public async Task Notification_deployment_script_is_idempotent_and_does_not_create_menu_items()
    {
        await using var db = new PreflightAcceptanceDatabase().CreateContext();
        var script = db.GetService<IMigrator>().GenerateScript("20261014020000_AddReceiptGlobalDiscountAmount",
            "20261014140000_AddBankNotificationBridge", MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("CREATE TABLE [BankNotificationReceipts]", script);
        Assert.Contains("IF NOT EXISTS", script); Assert.DoesNotContain("AdminMenuItems", script);
        var output = Path.Combine(Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName,
            "20261014140000_AddBankNotificationBridge.sql");
        await File.WriteAllTextAsync(output, script, new System.Text.UTF8Encoding(false));
    }
    [Fact]
    public async Task Notification_migration_and_receipt_dedup_work_on_isolated_SQL_database()
    {
        await using var database = new PreflightAcceptanceDatabase();
        int storeId;
        await using (var schema = database.CreateContext())
        {
            await schema.Database.MigrateAsync();
            var verified = await new SqlServerDatabaseBaselinePreflight(schema,
                new EfCoreDatabaseMigrationCatalog(schema), new SqlServerDatabaseObjectInventoryReader(schema),
                new EfCoreDatabaseSchemaManifestCatalog(schema), new SqlServerSchemaSnapshotReader(schema)).InspectAsync();
            Assert.Equal(DatabaseCompatibilityState.CurrentBaseline, verified.State);
            Assert.Equal(verified.SourceMigrationCount, verified.AppliedMigrationCount);
            var store = new Store { Name = "Bridge SQL test", SubDomain = "bridge-sql", SubDomainNormalized = "bridge-sql" };
            schema.Add(store); await schema.SaveChangesAsync(); storeId = store.Id;
        }
        var tenant = new TenantContext(); tenant.SetStore(storeId,"bridge-sql");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(database.ConnectionString).Options;
        await using var db = new AppDbContext(options, tenant, new CurrentUser());
        var bank = new StoreBankAccount { StoreId = storeId, BankCode = "ACB", BankName = "Test", AccountNumber = "TEST-SQL", AccountName = "Test" };
        db.Add(bank); await db.SaveChangesAsync();
        var device = new BankNotificationDevice { StoreId = storeId, BankAccountId = bank.Id, BankAccountFingerprint = BankNotificationParser.AccountFingerprint(bank), Name = "Test", TokenHash = new string('A',64) };
        db.Add(device); await db.SaveChangesAsync();
        var runtime = new Runtime();
        var notifier = System.Reflection.DispatchProxy.Create<IPosRealtimeNotifier, StubProxy>();
        ((StubProxy)(object)notifier).Call = (_,_) => Task.CompletedTask;
        var service = new BankNotificationBridgeService(db, runtime, new SqlAcbOrderLockProvider(), null!, notifier);
        var id = await service.AcceptAsync(device, Upload(pos:"Chuyen tien"), default);
        Assert.Equal(id, await service.AcceptAsync(device, Upload(pos:"Chuyen tien"), default));
        Assert.Single(await db.Set<BankNotificationReceipt>().ToListAsync());
        var receipt = await db.Set<BankNotificationReceipt>().SingleAsync();
        Assert.Equal(20000m, receipt.Amount); Assert.True(receipt.RowVersion.Length > 0); Assert.Null(receipt.PaymentId);
    }
}
