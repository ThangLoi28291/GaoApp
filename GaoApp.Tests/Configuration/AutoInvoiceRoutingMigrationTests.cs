using GaoApp.Infrastructure.Data.Migrations;
using GaoApp.Infrastructure.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GaoApp.Tests.Configuration;

public sealed class AutoInvoiceRoutingMigrationTests
{
    private const string MigrationId =
        "20260926113012_AddInvoiceIssuanceRoutingAndBuyerSelfService";

    [Fact]
    public void Migration_up_contains_only_the_approved_schema_and_reviewed_backfill()
    {
        var migration =
            new AddInvoiceIssuanceRoutingAndBuyerSelfService();

        var operations = migration.UpOperations;

        Assert.All(
            operations,
            operation =>
                Assert.True(
                    operation is AddColumnOperation
                        or CreateTableOperation
                        or CreateIndexOperation
                        or SqlOperation,
                    $"Unexpected migration operation: {operation.GetType().Name}"));

        var addedColumns =
            operations.OfType<AddColumnOperation>().ToList();

        Assert.Contains(
            addedColumns,
            x =>
                x.Table == "Orders" &&
                x.Name == "InvoiceIssuanceRoute");

        Assert.Contains(
            addedColumns,
            x =>
                x.Table == "Orders" &&
                x.Name == "InvoiceIssuanceRouteSelectedAtUtc");

        Assert.Contains(
            addedColumns,
            x =>
                x.Table == "Orders" &&
                x.Name == "InvoiceIssuanceRouteSelectedByUserId");

        Assert.Contains(
            addedColumns,
            x =>
                x.Table == "InvoiceHeads" &&
                x.Name == "LastIssuanceRelevantChangeAtUtc");

        Assert.Contains(
            addedColumns,
            x =>
                x.Table == "InvoiceHeads" &&
                x.Name == "BuyerCitizenId" &&
                x.MaxLength == 50);

        var table =
            Assert.Single(
                operations.OfType<CreateTableOperation>());

        Assert.Equal(
            "InvoiceBuyerSelfServiceRequests",
            table.Name);

        var columnNames =
            table.Columns
                .Select(x => x.Name)
                .ToHashSet(StringComparer.Ordinal);

        foreach (var expected in new[]
        {
            "Id",
            "StoreId",
            "OrderId",
            "TokenHash",
            "ExpiresAtUtc",
            "LastSubmittedAtUtc",
            "RevokedAtUtc",
            "CreatedAtUtc",
            "CreatedBy",
            "UpdatedAtUtc",
            "UpdatedBy",
            "IsDeleted",
            "DeletedAtUtc",
            "DeletedBy",
            "RowVersion"
        })
        {
            Assert.Contains(expected, columnNames);
        }

        var tokenHash =
            Assert.Single(
                table.Columns,
                x => x.Name == "TokenHash");

        Assert.Equal("binary(32)", tokenHash.ColumnType);
        Assert.False(tokenHash.IsNullable);

        Assert.Contains(
            table.ForeignKeys,
            x =>
                x.PrincipalTable == "Orders" &&
                x.Columns.SequenceEqual(["OrderId"]) &&
                x.PrincipalColumns.SequenceEqual(["Id"]) &&
                x.OnDelete == ReferentialAction.Restrict);

        Assert.Contains(
            table.ForeignKeys,
            x =>
                x.PrincipalTable == "Stores" &&
                x.Columns.SequenceEqual(["StoreId"]) &&
                x.PrincipalColumns.SequenceEqual(["Id"]));

        var indexes =
            operations.OfType<CreateIndexOperation>().ToList();

        Assert.Contains(
            indexes,
            x =>
                x.Table == "Orders" &&
                x.Columns.SequenceEqual(
                [
                    "StoreId",
                    "InvoiceIssuanceRoute",
                    "CompletedAtUtc",
                    "IsDeleted"
                ]));

        Assert.Contains(
            indexes,
            x =>
                x.Table == "InvoiceHeads" &&
                x.Columns.SequenceEqual(
                [
                    "StoreId",
                    "LastIssuanceRelevantChangeAtUtc",
                    "IsDeleted"
                ]));

        Assert.Contains(
            indexes,
            x =>
                x.Table == "AutoInvoiceOperationSources" &&
                x.Columns.SequenceEqual(
                [
                    "StoreId",
                    "InvoiceHeadId",
                    "Status",
                    "IsDeleted"
                ]));

        var uniqueTokenIndex =
            Assert.Single(
                indexes,
                x =>
                    x.Table ==
                        "InvoiceBuyerSelfServiceRequests" &&
                    x.Columns.SequenceEqual(["TokenHash"]));

        Assert.True(uniqueTokenIndex.IsUnique);

        Assert.Contains(
            indexes,
            x =>
                x.Table ==
                    "InvoiceBuyerSelfServiceRequests" &&
                x.Columns.SequenceEqual(
                [
                    "StoreId",
                    "OrderId",
                    "IsDeleted"
                ]));

        Assert.Contains(
            indexes,
            x =>
                x.Table ==
                    "InvoiceBuyerSelfServiceRequests" &&
                x.Columns.SequenceEqual(
                [
                    "StoreId",
                    "ExpiresAtUtc",
                    "IsDeleted"
                ]));

        var sql =
            Assert.Single(
                operations.OfType<SqlOperation>());

        Assert.False(sql.SuppressTransaction);

        Assert.Contains(
            "THROW 55410",
            sql.Sql,
            StringComparison.Ordinal);

        Assert.Contains(
            "[InvoiceIssuanceRoute] = 1",
            sql.Sql,
            StringComparison.Ordinal);

        Assert.Contains(
            "[InvoiceIssuanceRoute] = 2",
            sql.Sql,
            StringComparison.Ordinal);

        Assert.Contains(
            "[LastIssuanceRelevantChangeAtUtc]",
            sql.Sql,
            StringComparison.Ordinal);

        Assert.Contains(
            "[SalesReturns]",
            sql.Sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_down_is_fail_closed_before_removing_feature_schema()
    {
        var migration =
            new AddInvoiceIssuanceRoutingAndBuyerSelfService();

        Assert.NotEmpty(migration.DownOperations);

        var guard =
            Assert.IsType<SqlOperation>(
                migration.DownOperations[0]);

        Assert.Contains(
            "THROW 55411",
            guard.Sql,
            StringComparison.Ordinal);

        Assert.Contains(
            "[InvoiceBuyerSelfServiceRequests]",
            guard.Sql,
            StringComparison.Ordinal);

        Assert.Contains(
            "[InvoiceIssuanceRouteSelectedAtUtc]",
            guard.Sql,
            StringComparison.Ordinal);

        Assert.All(
            migration.DownOperations,
            operation =>
                Assert.True(
                    operation is SqlOperation
                        or DropTableOperation
                        or DropIndexOperation
                        or DropColumnOperation,
                    $"Unexpected down operation: {operation.GetType().Name}"));

        Assert.Contains(
            migration.DownOperations.OfType<DropTableOperation>(),
            x =>
                x.Name ==
                    "InvoiceBuyerSelfServiceRequests");

        Assert.Contains(
            migration.DownOperations.OfType<DropIndexOperation>(),
            x =>
                x.Table ==
                    "AutoInvoiceOperationSources" &&
                x.Name ==
                    "IX_AutoInvoiceOperationSources_StoreId_InvoiceHeadId_Status_IsDeleted");

        var droppedColumns =
            migration.DownOperations
                .OfType<DropColumnOperation>()
                .Select(x => $"{x.Table}.{x.Name}")
                .ToHashSet(StringComparer.Ordinal);

        foreach (var expected in new[]
        {
            "Orders.InvoiceIssuanceRoute",
            "Orders.InvoiceIssuanceRouteSelectedAtUtc",
            "Orders.InvoiceIssuanceRouteSelectedByUserId",
            "InvoiceHeads.LastIssuanceRelevantChangeAtUtc",
            "InvoiceHeads.BuyerCitizenId"
        })
        {
            Assert.Contains(expected, droppedColumns);
        }
    }

    [Fact]
    public async Task Current_model_manifest_and_migration_inventory_include_routing_schema_once()
    {
        await using var database =
            new PreflightAcceptanceDatabase();

        using var db =
            database.CreateContext();

        var migrations =
            db.Database.GetMigrations().ToList();

        Assert.Equal(43, migrations.Count);
        Assert.Equal(MigrationId, migrations[^1]);
        Assert.Single(
            migrations,
            x => x == MigrationId);

        Assert.False(
            db.Database.HasPendingModelChanges());

        Assert.NotNull(
            db.Model.FindEntityType(
                "GaoApp.Domain.Entities.InvoiceBuyerSelfServiceRequest"));

        var manifest =
            new EfCoreDatabaseSchemaManifestCatalog(db)
                .GetCurrentManifest();

        Assert.Equal(MigrationId, manifest.AppliedMigrationIds[^1]);

        var selfServiceTable =
            Assert.Single(
                manifest.Tables,
                x =>
                    x.Identity.Name ==
                    "invoicebuyerselfservicerequests");

        Assert.Contains(
            selfServiceTable.Indexes,
            x =>
                x.IsUnique &&
                x.KeyColumns
                    .Select(c => c.Name)
                    .SequenceEqual(["tokenhash"]));

        Assert.Contains(
            selfServiceTable.Indexes,
            x =>
                x.KeyColumns
                    .Select(c => c.Name)
                    .SequenceEqual(
                    [
                        "storeid",
                        "orderid",
                        "isdeleted"
                    ]));

        var orderTable =
            Assert.Single(
                manifest.Tables,
                x => x.Identity.Name == "orders");

        Assert.Contains(
            orderTable.Columns,
            x => x.Name == "invoiceissuanceroute");

        var invoiceHeadTable =
            Assert.Single(
                manifest.Tables,
                x => x.Identity.Name == "invoiceheads");

        Assert.Contains(
            invoiceHeadTable.Columns,
            x =>
                x.Name ==
                    "lastissuancerelevantchangeatutc");

        Assert.Contains(
            invoiceHeadTable.Columns,
            x =>
                x.Name ==
                    "buyercitizenid");

        var sourceTable =
            Assert.Single(
                manifest.Tables,
                x =>
                    x.Identity.Name ==
                    "autoinvoiceoperationsources");

        Assert.Contains(
            sourceTable.Indexes,
            x =>
                x.KeyColumns
                    .Select(c => c.Name)
                    .SequenceEqual(
                    [
                        "storeid",
                        "invoiceheadid",
                        "status",
                        "isdeleted"
                    ]));
    }
}
