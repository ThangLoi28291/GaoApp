using GaoApp.Infrastructure.Data.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GaoApp.Tests.Configuration;

[Collection("R1FinalDatabasePreflight")]
public sealed class DatabaseSchemaInspectionTests
{
    [Fact]
    public async Task Reports_actual_drift_without_applying_pending_migrations_or_modifying_data()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using var db = database.CreateContext();
        await db.GetService<IMigrator>().MigrateAsync("20260926113012_AddInvoiceIssuanceRoutingAndBuyerSelfService");
        var inspector = new DatabaseSchemaInspection(db);
        var before = await inspector.ReadAsync();
        Assert.Equal("MatchingAppliedPrefix", before.Status);
        Assert.NotEmpty(before.PendingMigrations);
        Assert.Equal((await db.Database.GetPendingMigrationsAsync()).ToArray(), before.PendingMigrations);
        Assert.Empty(before.ExpectedOnly);
        Assert.Empty(before.ActualOnly);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE dbo.SchemaInspectProbe (Id int NOT NULL PRIMARY KEY, Marker nvarchar(40) NULL); INSERT dbo.SchemaInspectProbe VALUES (1,N'KEEP_THIS_ROW');");
        var drift = await inspector.ReadAsync();
        Assert.Equal("StructuralSchemaMismatch", drift.Status);
        Assert.Contains(drift.ActualOnly, x => x.StartsWith("table|") && x.Contains("schemainspectprobe"));
        Assert.Empty(drift.ExpectedOnly);
        Assert.Equal(before.AppliedMigrations, drift.AppliedMigrations);
        Assert.Equal(before.PendingMigrations, drift.PendingMigrations);
        Assert.DoesNotContain("KEEP_THIS_ROW", string.Join('\n', drift.ActualOnly));
        Assert.Equal("KEEP_THIS_ROW", await db.Database.SqlQueryRaw<string>("SELECT Marker AS [Value] FROM dbo.SchemaInspectProbe WHERE Id=1").SingleAsync());
        await db.Database.ExecuteSqlRawAsync("DROP TABLE dbo.SchemaInspectProbe");
        await db.Database.MigrateAsync();
        var current = await inspector.ReadAsync();
        Assert.Equal("MatchingCurrentSchema", current.Status);
        Assert.Empty(current.PendingMigrations);
        Assert.Empty(current.ExpectedOnly);
        Assert.Empty(current.ActualOnly);
    }
}
