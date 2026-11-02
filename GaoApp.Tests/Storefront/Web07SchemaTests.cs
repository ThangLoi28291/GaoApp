using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data.Migrations;
using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb07"), Trait("Category", "StorefrontWeb07")]
public sealed class Web07SchemaTests(Web01Fixture fixture)
{
    // Pure type metadata only: discovery cannot open SQL or instantiate a fixture.
    public static IEnumerable<object[]> NativeRecords => new[]
    {
        typeof(StorefrontCancellationRequest), typeof(StorefrontCancellationDecision), typeof(StorefrontCancellationReviewAcknowledgement),
        typeof(StorefrontRefundFunding), typeof(StorefrontRefundDecision), typeof(StorefrontQrClosureProof), typeof(StorefrontBankReviewRestriction),
        typeof(StorefrontBankScanAttempt), typeof(StorefrontBankScanCompletion)
    }.Select(type => new object[] { type });

    [Theory, MemberData(nameof(NativeRecords))]
    public void Every_native_cancellation_refund_record_has_actual_tenant_parent_rowversion_and_restrictive_source_scope(Type type)
    {
        using var db = fixture.Database.CreateHostContext(); var entity = db.Model.FindEntityType(type); Assert.NotNull(entity);
        Assert.NotEmpty(entity.GetDeclaredQueryFilters()); Assert.Equal("StoreId", entity.FindProperty("StoreId")!.Name);
        Assert.DoesNotContain(entity.GetProperties(), p => p.Name == "StoreId1");
        var version = entity.FindProperty("RowVersion")!; Assert.True(version.IsConcurrencyToken); Assert.Equal(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate, version.ValueGenerated);
        var store = Assert.Single(entity.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(Store));
        Assert.Equal(new[] { "StoreId" }, store.Properties.Select(x => x.Name)); Assert.Equal(DeleteBehavior.NoAction, store.DeleteBehavior);
        var parent = Assert.Single(entity.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(StorefrontOrder));
        Assert.Equal(new[] { "StoreId", "StorefrontOrderId" }, parent.Properties.Select(x => x.Name));
        Assert.Equal(new[] { "StoreId", "Id" }, parent.PrincipalKey.Properties.Select(x => x.Name));
        foreach (var fk in entity.GetForeignKeys().Where(x => x.PrincipalEntityType.ClrType != typeof(Store)))
        {
            Assert.Contains("StoreId", fk.Properties.Select(x => x.Name)); Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
        }
    }

    [Fact]
    public async Task Fresh_actual_schema_matches_complete_manifest_with_all_trusted_guards_no_shadow_store_and_no_store_menu_or_money_seed()
    {
        await using var sql = new InventoryPostingLocalDb(); await sql.MigrateAsync(); await using var db = sql.CreateHostContext();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync()); Assert.False(db.Database.HasPendingModelChanges());
        await db.Database.OpenConnectionAsync(); var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var expected = new EfCoreDatabaseSchemaManifestCatalog(db).GetCurrentManifest();
        var actual = await new SqlServerSchemaSnapshotReader(db).ReadAsync(applied); var comparison = DatabaseSchemaComparer.Compare(expected, actual);
        var directory = Web07Support.Artifact("schema/" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "expected.json"), JsonSerializer.Serialize(expected));
        await File.WriteAllTextAsync(Path.Combine(directory, "live.json"), JsonSerializer.Serialize(actual));
        await File.WriteAllTextAsync(Path.Combine(directory, "comparison.json"), JsonSerializer.Serialize(comparison));
        await File.WriteAllTextAsync(Path.Combine(directory, "raw-checks.json"), await db.Database.SqlQueryRaw<string>("SELECT (SELECT OBJECT_NAME(parent_object_id) AS TableName,name,definition,is_disabled,is_not_trusted FROM sys.check_constraints ORDER BY parent_object_id,name FOR JSON PATH) AS [Value]").SingleAsync());
        await File.WriteAllTextAsync(Path.Combine(directory, "raw-triggers.json"), await db.Database.SqlQueryRaw<string>("SELECT (SELECT OBJECT_NAME(t.parent_id) AS TableName,t.name,t.is_disabled,m.definition FROM sys.triggers t JOIN sys.sql_modules m ON m.object_id=t.object_id WHERE t.parent_class=1 ORDER BY t.parent_id,t.name FOR JSON PATH) AS [Value]").SingleAsync());
        Assert.True(comparison.IsMatch, JsonSerializer.Serialize(comparison.Mismatches));
        Assert.Equal(0, await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.columns WHERE name=N'StoreId1'"));
        Assert.Equal(0, await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.check_constraints WHERE is_disabled=1 OR is_not_trusted=1"));
        Assert.Equal(0, await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled=1 OR is_not_trusted=1"));
        foreach (var item in NativeRecords)
        {
            var type = (Type)item[0]; var table = db.Model.FindEntityType(type)!.GetTableName();
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT CONVERT(int,COUNT(*)) AS [Value] FROM sys.tables WHERE name={0}", table!).SingleAsync());
        }
        Assert.Empty(await db.Stores.ToListAsync()); Assert.Empty(await db.AdminMenuItems.ToListAsync()); Assert.Empty(await db.RolePermissions.ToListAsync());
        Assert.Empty(await db.StorefrontOrders.ToListAsync()); Assert.Empty(await db.OrderPayments.ToListAsync()); Assert.Empty(await db.POSShiftCashTransactions.ToListAsync());
        Assert.Empty(await db.TreasuryEntries.ToListAsync()); Assert.Empty(await db.Set<StorefrontRefundEvidence>().ToListAsync());
        Assert.Empty(await db.Set<BankVerifiedReceipt>().ToListAsync()); Assert.Empty(await db.InventoryTransactions.ToListAsync());
    }
}
