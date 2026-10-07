using GaoApp.Infrastructure.Data.Migrations;
using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GaoApp.Tests.Promotions;

public sealed class MixedQuantityPromotionSchemaTests
{
    [Fact]
    public void Migration_adds_only_group_configuration_and_defaults_existing_combos_to_required_items()
    {
        using var db = new PreflightAcceptanceDatabase().CreateContext();
        var assembly = db.GetService<IMigrationsAssembly>();
        var migration = assembly.CreateMigration(
            assembly.Migrations["20261007090000_AddMixedQuantityPromotions"], db.Database.ProviderName!);
        Assert.Equal(3, migration.UpOperations.Count);
        Assert.All(migration.UpOperations, operation => Assert.IsType<AddColumnOperation>(operation));
        var columns = migration.UpOperations.Cast<AddColumnOperation>().ToArray();
        Assert.All(columns, column => Assert.Equal("Promotions", column.Table));
        Assert.Equal((byte)1, columns.Single(x => x.Name == "ComboPricingMode").DefaultValue);
        Assert.All(columns.Where(x => x.Name != "ComboPricingMode"), x => Assert.True(x.IsNullable));
        Assert.Equal(3, migration.DownOperations.Count);
        var manifest = new EfCoreDatabaseSchemaManifestCatalog(db).GetCurrentManifest();
        Assert.Contains(manifest.Tables.Single(x => x.Identity.Name == "promotions").Columns,
            x => x.Name == "comboquantity" && x.Scale == 4);
    }
}
