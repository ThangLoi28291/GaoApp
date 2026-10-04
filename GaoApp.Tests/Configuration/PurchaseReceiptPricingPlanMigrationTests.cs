using GaoApp.Infrastructure.Migrations;
using GaoApp.Infrastructure.Data.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GaoApp.Tests.Configuration;

[Collection("R1FinalDatabasePreflight")]
public sealed class PurchaseReceiptPricingPlanMigrationTests
{
    [Fact]
    public async Task Pricing_and_bill_schema_matches_canonical_manifest_exactly()
    {
        await using var database = new InventoryPostingLocalDb(); await database.MigrateAsync();
        await using var db = database.CreateHostContext(); await db.Database.OpenConnectionAsync();
        var expected = new EfCoreDatabaseSchemaManifestCatalog(db).GetCurrentManifest();
        var actual = await new SqlServerSchemaSnapshotReader(db).ReadAsync(expected.AppliedMigrationIds);
        var expectedPricing = DatabaseSchemaCanonicalizer.WithFingerprint(expected.AppliedMigrationIds,
            expected.Tables.Where(x => x.Identity.Name.StartsWith("purchasereceiptpricing") || x.Identity.Name is "purchasereceiptgiftvaluation" or "purchasereceiptbillline").ToArray(), []);
        var actualPricing = DatabaseSchemaCanonicalizer.WithFingerprint(actual.AppliedMigrationIds,
            actual.Tables.Where(x => x.Identity.Name.StartsWith("purchasereceiptpricing") || x.Identity.Name is "purchasereceiptgiftvaluation" or "purchasereceiptbillline").ToArray(), []);
        Assert.Equal(6, expectedPricing.Tables.Count); Assert.Equal(6, actualPricing.Tables.Count);
        var expectedRecords = DatabaseSchemaCanonicalizer.CreateCategoryRecords(expectedPricing).All;
        var actualRecords = DatabaseSchemaCanonicalizer.CreateCategoryRecords(actualPricing).All;
        var differences = expectedRecords.Except(actualRecords).Select(x => "Expected: " + x).Concat(actualRecords.Except(expectedRecords).Select(x => "Actual: " + x)).ToArray();
        Assert.True(DatabaseSchemaComparer.Compare(expectedPricing, actualPricing).IsMatch, string.Join("\n", differences));
    }

    [Fact]
    public void Reserved_migration_only_creates_five_normalized_tables_with_tenant_graph_constraints()
    {
        var migration = new AddPurchaseReceiptPricingPlans();
        var tables = migration.UpOperations.OfType<CreateTableOperation>().ToArray();
        var expected = new HashSet<string> { "PurchaseReceiptPricingPlan", "PurchaseReceiptPricingPlanLine", "PurchaseReceiptPricingRule", "PurchaseReceiptPricingRuleSource", "PurchaseReceiptGiftValuation" };
        Assert.Equal(5, tables.Length); Assert.True(expected.SetEquals(tables.Select(x => x.Name)));
        Assert.All(migration.UpOperations, x => Assert.True(x is CreateTableOperation || x is CreateIndexOperation index && expected.Contains(index.Table)));
        Assert.All(tables, table =>
        {
            Assert.Contains(table.Columns, x => x.Name == "RowVersion" && x.IsRowVersion);
            Assert.Contains(table.Columns, x => x.Name == "StoreId");
            Assert.NotEmpty(table.CheckConstraints); Assert.NotNull(table.PrimaryKey);
            Assert.Contains(table.ForeignKeys, x => x.PrincipalTable == "Stores");
        });
        Assert.Contains(tables.Single(x => x.Name == "PurchaseReceiptPricingRuleSource").ForeignKeys,
            x => x.Columns.SequenceEqual(new[] { "StoreId", "PricingPlanId", "PricingPlanLineId" }));
        Assert.Equal(5, migration.DownOperations.Count);
        Assert.All(migration.DownOperations, x => Assert.IsType<DropTableOperation>(x));
    }

    [Fact]
    public void Bill_migration_only_extends_pricing_graph_and_enforces_tenant_bill_links()
    {
        var migration = new AddPurchaseReceiptBillLines();
        var table = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal("PurchaseReceiptBillLine", table.Name);
        Assert.Contains(table.ForeignKeys, x => x.PrincipalTable == "PurchaseReceiptPricingPlan" && x.Columns.SequenceEqual(new[] { "StoreId", "PricingPlanId" }));
        var allowed = new HashSet<string> { "PurchaseReceiptBillLine", "PurchaseReceiptPricingPlan", "PurchaseReceiptPricingRule", "PurchaseReceiptPricingRuleSource" };
        Assert.All(migration.UpOperations, operation =>
        {
            var name = operation switch
            {
                CreateTableOperation x => x.Name,
                AddColumnOperation x => x.Table,
                AddForeignKeyOperation x => x.Table,
                AddCheckConstraintOperation x => x.Table,
                CreateIndexOperation x => x.Table,
                DropIndexOperation x => x.Table,
                _ => null
            };
            Assert.True(name is not null && allowed.Contains(name), operation.GetType().Name);
        });
        Assert.Contains(migration.UpOperations.OfType<AddForeignKeyOperation>(), x => x.Table == "PurchaseReceiptPricingRuleSource" && x.PrincipalTable == "PurchaseReceiptBillLine" && x.Columns.SequenceEqual(new[] { "StoreId", "PricingPlanId", "BillLineId" }));
    }
}
