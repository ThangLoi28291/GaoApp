using FluentAssertions;
using GaoApp.Infrastructure.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GaoApp.Tests.Configuration;

[Collection("R1FinalDatabasePreflight")]
public sealed class InputInvoiceItemCatalogMappingMigrationTests
{
    private const string PreviousMigrationId =
        "20260824150000_AddInputInvoiceBuyerOwnerGuard";
    private const string MappingMigrationId =
        "20260826150000_AddInputInvoiceItemCatalogMapping";

    [Fact]
    public void Focused_mapping_migration_is_the_only_new_migration()
    {
        new AddInputInvoiceItemCatalogMapping()
            .Should().BeAssignableTo<Migration>();
    }

    [Fact]
    public async Task Legacy_backfill_is_deterministic_and_does_not_synthesize_reusable_maps()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync(PreviousMigrationId);

        await database.ExecuteAsync("""
            INSERT INTO [dbo].[Stores]
                ([Name],[SubDomain],[SubDomainNormalized],[IsActive],
                 [CreatedAtUtc],[IsDeleted])
            VALUES (N'ITEM-MAP Test Store',N'item-map-test',N'ITEM-MAP-TEST',1,
                    SYSUTCDATETIME(),0);
            DECLARE @storeId int = SCOPE_IDENTITY();
            INSERT INTO [dbo].[InputInvoiceHead]
                ([StoreId],[InvoiceNumber],[TotalBeforeTax],[TotalTaxAmount],
                 [TotalPaymentAmount],[CreatedAtUtc],[IsDeleted])
            VALUES (@storeId,N'LEGACY-ITEM-MAP',0,0,0,SYSUTCDATETIME(),0);
            DECLARE @headId int = SCOPE_IDENTITY();
            INSERT INTO [dbo].[InputInvoiceDetail]
                ([InputInvoiceHeadId],[LineNo],[ItemName],[UnitName],[Quantity],
                 [UnitPrice],[LineAmount],[VatAmount],[CreatedAtUtc],[IsDeleted])
            VALUES (@headId,1,N'  Gạo' + NCHAR(9) + N'ST25  ',
                    N' Thùng  20 ',1,0,0,0,SYSUTCDATETIME(),0);
            """);

        await database.MigrateAsync();

        (await database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM [dbo].[InputInvoiceItemCatalogMap];"))
            .Should().Be(0);
        (await database.ExecuteScalarAsync<string>("""
            SELECT [NormalizedItemName] + N'|' + [NormalizedUnitName]
            FROM [dbo].[InputInvoiceDetail]
            WHERE [ItemName] LIKE N'%ST25%';
            """))
            .Should().Be("GẠO ST25|THÙNG 20");
        (await database.ExecuteScalarAsync<int>($"""
            SELECT COUNT(*) FROM [dbo].[__EFMigrationsHistory]
            WHERE [MigrationId] = N'{MappingMigrationId}';
            """))
            .Should().Be(1);
    }
}
