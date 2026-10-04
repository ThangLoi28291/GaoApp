using GaoApp.Application.Common.Options;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Migrations;
using GaoApp.Infrastructure.Data.Seed;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Configuration;

[Collection("R1FinalDatabasePreflight")]
public sealed class DatabaseSchemaManifestTests
{
    private const string BaselineMigrationId =
        "20260726073029_InitialProductionBaseline";

    private const string InventoryPostingMigrationId =
        "20260801110856_AddInventoryPostingIdempotency";

    private const string PurchaseReceiptAuditMigrationId =
        "20260814090000_AddPurchaseReceiptAuditEvents";
    private const string PurchaseReceiptCostPolicyMigrationId =
        "20260817090000_AddPurchaseReceiptCostCapitalizationPolicy";
    private const string InputInvoiceIdentityMigrationId =
        "20260817150000_AddInputInvoiceIdentityUniqueness";
    private const string InputInvoiceSupplierResolutionMigrationId =
        "20260822090000_AddInputInvoiceSupplierResolution";
    private const string InputInvoiceBuyerOwnerGuardMigrationId =
        "20260824150000_AddInputInvoiceBuyerOwnerGuard";
    private const string InputInvoiceItemCatalogMappingMigrationId =
        "20260826150000_AddInputInvoiceItemCatalogMapping";
    private const string InputInvoiceReconciliationMigrationId =
        "20260827150000_AddInputInvoiceReconciliation";
    private const string InputInvoiceSingleActiveReceiptMigrationId =
        "20260828150000_EnforceSingleActiveInputInvoicePerReceipt";
    private const string ReceivingWorkbenchMigrationId =
        "20260830112901_AddReceivingWorkbench";
    private const string ProvisionalReceivingItemsMigrationId =
        "20260831135031_AddProvisionalReceivingItems";
    private const string AcbPaymentsMigrationId =
        "20260908151919_AddStoreAcbPayments";
    private const string AcbCallbackInboxMigrationId =
        "20260908155844_AddAcbCallbackInbox";

    [Fact]
    public async Task Current_model_and_migration_snapshot_have_no_differences()
    {
        // Metadata-only check: do not connect to SQL Server just to clean up
        // a database which this test never creates.
        await using var db = new PreflightAcceptanceDatabase().CreateContext();
        var currentModel = db.GetService<IDesignTimeModel>()
            .Model.GetRelationalModel();
        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!;
        var snapshotModel = db.GetService<IModelRuntimeInitializer>()
            .Initialize(
                snapshot.Model,
                designTime: true,
                validationLogger: null)
            .GetRelationalModel();

        var differences = db.GetService<IMigrationsModelDiffer>()
            .GetDifferences(snapshotModel, currentModel);
        differences.Should().BeEmpty(
            string.Join(", ", differences.Select(x => x.GetType().Name)));
    }

    [Fact]
    public Task Current_history_missing_required_column_should_be_rejected()
        => AssertCorruptionRejectedAsync(
            "ALTER TABLE [dbo].[Stores] DROP COLUMN [Name];");

    [Fact]
    public Task Current_history_unexpected_extra_column_should_be_rejected()
        => AssertCorruptionRejectedAsync(
            "ALTER TABLE [dbo].[Stores] ADD [AcceptanceExtra] int NULL;");

    [Fact]
    public Task Current_history_missing_inventory_idempotency_column_should_be_rejected()
        => AssertCorruptionRejectedAsync(
            """
            DROP INDEX
                [UX_InventoryTransactions_StoreId_IdempotencyKey_Active]
            ON [dbo].[InventoryTransactions];

            ALTER TABLE [dbo].[InventoryTransactions]
                DROP COLUMN [IdempotencyKey];
            """,
            mismatches =>
            {
                mismatches.Columns.Should().BeGreaterThan(0);
                mismatches.Indexes.Should().BeGreaterThan(0);
                mismatches.Tables.Should().Be(0);
                mismatches.PrimaryKeys.Should().Be(0);
                mismatches.ForeignKeys.Should().Be(0);
                mismatches.CheckConstraints.Should().Be(0);
                mismatches.Sequences.Should().Be(0);
            },
            expectedMismatchCategoryCount: 2);

    [Fact]
    public Task Current_history_wrong_inventory_idempotency_column_type_should_be_rejected()
        => AssertOnlyColumnCorruptionRejectedAsync("""
            DROP INDEX
                [UX_InventoryTransactions_StoreId_IdempotencyKey_Active]
            ON [dbo].[InventoryTransactions];

            ALTER TABLE [dbo].[InventoryTransactions]
                ALTER COLUMN [IdempotencyKey] varbinary(31) NULL;

            CREATE UNIQUE INDEX
                [UX_InventoryTransactions_StoreId_IdempotencyKey_Active]
            ON [dbo].[InventoryTransactions]
                ([StoreId], [IdempotencyKey])
            WHERE [IdempotencyKey] IS NOT NULL
              AND [IsDeleted] = 0;
            """);

    [Fact]
    public Task Current_history_non_nullable_inventory_idempotency_column_should_be_rejected()
        => AssertOnlyColumnCorruptionRejectedAsync("""
            DROP INDEX
                [UX_InventoryTransactions_StoreId_IdempotencyKey_Active]
            ON [dbo].[InventoryTransactions];

            ALTER TABLE [dbo].[InventoryTransactions]
                ALTER COLUMN [IdempotencyKey] varbinary(32) NOT NULL;

            CREATE UNIQUE INDEX
                [UX_InventoryTransactions_StoreId_IdempotencyKey_Active]
            ON [dbo].[InventoryTransactions]
                ([StoreId], [IdempotencyKey])
            WHERE [IdempotencyKey] IS NOT NULL
              AND [IsDeleted] = 0;
            """);

    [Fact]
    public Task Current_history_inventory_idempotency_column_with_default_should_be_rejected()
        => AssertOnlyColumnCorruptionRejectedAsync("""
            ALTER TABLE [dbo].[InventoryTransactions]
            ADD CONSTRAINT
                [DF_Acceptance_InventoryTransactions_IdempotencyKey]
            DEFAULT (0x00) FOR [IdempotencyKey];
            """);

    [Fact]
    public Task Current_history_wrong_column_type_should_be_rejected()
        => AssertCorruptionRejectedAsync(
            "ALTER TABLE [dbo].[Stores] ALTER COLUMN [Name] nvarchar(201) NOT NULL;");

    [Fact]
    public Task Current_history_wrong_nullability_should_be_rejected()
        => AssertCorruptionRejectedAsync(
            "ALTER TABLE [dbo].[Stores] ALTER COLUMN [Name] nvarchar(200) NULL;");

    [Fact]
    public Task Current_history_missing_primary_key_should_be_rejected()
        => AssertCorruptionRejectedAsync(
            "ALTER TABLE [dbo].[AuditLogs] DROP CONSTRAINT [PK_AuditLogs];");

    [Fact]
    public Task Current_history_missing_foreign_key_should_be_rejected()
        => AssertCorruptionRejectedAsync("""
            ALTER TABLE [dbo].[CustomerRewardLedgers]
            DROP CONSTRAINT
                [FK_CustomerRewardLedgers_CustomerRewardVouchers_VoucherId];
            """);

    [Fact]
    public Task Current_history_wrong_foreign_key_delete_action_should_be_rejected()
        => AssertCorruptionRejectedAsync("""
            ALTER TABLE [dbo].[CustomerRewardLedgers]
            DROP CONSTRAINT
                [FK_CustomerRewardLedgers_CustomerRewardVouchers_VoucherId];

            ALTER TABLE [dbo].[CustomerRewardLedgers]
            ADD CONSTRAINT
                [FK_CustomerRewardLedgers_CustomerRewardVouchers_VoucherId]
            FOREIGN KEY ([VoucherId])
            REFERENCES [dbo].[CustomerRewardVouchers] ([Id])
            ON DELETE CASCADE;
            """);

    [Fact]
    public Task Current_history_missing_unique_index_should_be_rejected()
        => AssertCorruptionRejectedAsync(
            "DROP INDEX [IX_Stores_SubDomainNormalized] ON [dbo].[Stores];");

    [Fact]
    public Task Current_history_wrong_index_filter_should_be_rejected()
        => AssertCorruptionRejectedAsync("""
            DROP INDEX
                [IX_InvoiceDetails_StoreId_InvoiceHeadId_OrderLegalEntityAllocationId]
            ON [dbo].[InvoiceDetails];

            CREATE UNIQUE INDEX
                [IX_InvoiceDetails_StoreId_InvoiceHeadId_OrderLegalEntityAllocationId]
            ON [dbo].[InvoiceDetails]
                ([StoreId], [InvoiceHeadId], [OrderLegalEntityAllocationId])
            WHERE [OrderLegalEntityAllocationId] IS NOT NULL
              AND [IsDeleted] = 1;
            """);

    [Theory]
    [InlineData("")]
    [InlineData("CREATE INDEX [IX_InventoryTransactions_LedgerTimeline] ON [dbo].[InventoryTransactions] ([StoreId], [OccurredAtUtc], [Id] DESC) INCLUDE ([QuantityChange], [AfterQty]) WHERE [IsDeleted] = 0;")]
    [InlineData("CREATE INDEX [IX_InventoryTransactions_LedgerTimeline] ON [dbo].[InventoryTransactions] ([StoreId], [OccurredAtUtc] DESC, [Id] DESC) INCLUDE ([QuantityChange]) WHERE [IsDeleted] = 0;")]
    [InlineData("CREATE INDEX [IX_InventoryTransactions_LedgerTimeline] ON [dbo].[InventoryTransactions] ([StoreId], [OccurredAtUtc] DESC, [Id] DESC) INCLUDE ([QuantityChange], [AfterQty]) WHERE [IsDeleted] = 1;")]
    public Task Current_history_missing_or_changed_timeline_index_should_be_rejected(string replacementSql)
        => AssertCorruptionRejectedAsync(
            "DROP INDEX [IX_InventoryTransactions_LedgerTimeline] ON [dbo].[InventoryTransactions]; "
            + replacementSql,
            expectedChangedIndex: "ix_inventorytransactions_ledgertimeline");

    [Fact]
    public async Task Timeline_index_matches_sql_server_metadata()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using var db = database.CreateContext();
        await new EfCoreDatabaseMigrationExecutor(db).MigrateAsync();
        await db.Database.OpenConnectionAsync();
        var snapshot = await new SqlServerSchemaSnapshotReader(db)
            .ReadAsync(await database.ReadMigrationHistoryAsync());
        var index = snapshot.Tables.Single(table => table.Identity
                == new DatabaseObjectIdentity("dbo", "InventoryTransactions"))
            .Indexes.Single(item => item.Name == "ix_inventorytransactions_ledgertimeline");
        index.KeyColumns.Should().Equal(
            new DatabaseIndexColumnSchema("storeid", false),
            new DatabaseIndexColumnSchema("occurredatutc", true),
            new DatabaseIndexColumnSchema("id", true));
        index.IncludedColumns.Should().BeEquivalentTo(new[] { "quantitychange", "afterqty" });
        index.Filter.Should().Be(DatabaseSchemaNormalization.NormalizeSqlExpression("[IsDeleted] = 0"));
        index.IsUnique.Should().BeFalse();
        index.IsClustered.Should().BeFalse();
        index.IsDisabled.Should().BeFalse();
    }

    [Theory]
    [InlineData("'Apply'")]
    [InlineData("'Apply','Void','Restore'")]
    public Task Current_history_changed_deposit_filter_values_should_be_rejected(string kinds)
        => AssertCorruptionRejectedAsync(
            "DROP INDEX [IX_CustomerDepositEntries_StoreId_OrderId_Kind] ON [dbo].[CustomerDepositEntries]; "
            + "CREATE UNIQUE INDEX [IX_CustomerDepositEntries_StoreId_OrderId_Kind] "
            + "ON [dbo].[CustomerDepositEntries] ([StoreId], [OrderId], [Kind]) "
            + $"WHERE [OrderId] IS NOT NULL AND [Kind] IN ({kinds});",
            expectedChangedIndex: "ix_customerdepositentries_storeid_orderid_kind");

    [Fact]
    public Task Current_history_wrong_inventory_idempotency_index_filter_should_be_rejected()
        => AssertOnlyIndexCorruptionRejectedAsync("""
            DROP INDEX
                [UX_InventoryTransactions_StoreId_IdempotencyKey_Active]
            ON [dbo].[InventoryTransactions];

            CREATE UNIQUE INDEX
                [UX_InventoryTransactions_StoreId_IdempotencyKey_Active]
            ON [dbo].[InventoryTransactions]
                ([StoreId], [IdempotencyKey])
            WHERE [IdempotencyKey] IS NOT NULL
              AND [IsDeleted] = 1;
            """);

    [Fact]
    public Task Current_history_inventory_idempotency_index_with_reversed_key_columns_should_be_rejected()
        => AssertOnlyIndexCorruptionRejectedAsync("""
            DROP INDEX
                [UX_InventoryTransactions_StoreId_IdempotencyKey_Active]
            ON [dbo].[InventoryTransactions];

            CREATE UNIQUE INDEX
                [UX_InventoryTransactions_StoreId_IdempotencyKey_Active]
            ON [dbo].[InventoryTransactions]
                ([IdempotencyKey], [StoreId])
            WHERE [IdempotencyKey] IS NOT NULL
              AND [IsDeleted] = 0;
            """);

    [Fact]
    public Task Current_history_inventory_idempotency_index_missing_store_id_should_be_rejected()
        => AssertOnlyIndexCorruptionRejectedAsync("""
            DROP INDEX
                [UX_InventoryTransactions_StoreId_IdempotencyKey_Active]
            ON [dbo].[InventoryTransactions];

            CREATE UNIQUE INDEX
                [UX_InventoryTransactions_StoreId_IdempotencyKey_Active]
            ON [dbo].[InventoryTransactions]
                ([IdempotencyKey])
            WHERE [IdempotencyKey] IS NOT NULL
              AND [IsDeleted] = 0;
            """);

    [Fact]
    public Task Current_history_inventory_idempotency_index_missing_idempotency_key_should_be_rejected()
        => AssertOnlyIndexCorruptionRejectedAsync("""
            DROP INDEX
                [UX_InventoryTransactions_StoreId_IdempotencyKey_Active]
            ON [dbo].[InventoryTransactions];

            CREATE UNIQUE INDEX
                [UX_InventoryTransactions_StoreId_IdempotencyKey_Active]
            ON [dbo].[InventoryTransactions]
                ([StoreId])
            WHERE [IdempotencyKey] IS NOT NULL
              AND [IsDeleted] = 0;
            """);

    [Fact]
    public Task Current_history_non_unique_inventory_idempotency_index_should_be_rejected()
        => AssertOnlyIndexCorruptionRejectedAsync("""
            DROP INDEX
                [UX_InventoryTransactions_StoreId_IdempotencyKey_Active]
            ON [dbo].[InventoryTransactions];

            CREATE INDEX
                [UX_InventoryTransactions_StoreId_IdempotencyKey_Active]
            ON [dbo].[InventoryTransactions]
                ([StoreId], [IdempotencyKey])
            WHERE [IdempotencyKey] IS NOT NULL
              AND [IsDeleted] = 0;
            """);

    [Fact]
    public Task Current_history_inventory_idempotency_index_without_filter_should_be_rejected()
        => AssertOnlyIndexCorruptionRejectedAsync("""
            DROP INDEX
                [UX_InventoryTransactions_StoreId_IdempotencyKey_Active]
            ON [dbo].[InventoryTransactions];

            CREATE UNIQUE INDEX
                [UX_InventoryTransactions_StoreId_IdempotencyKey_Active]
            ON [dbo].[InventoryTransactions]
                ([StoreId], [IdempotencyKey]);
            """);

    [Fact]
    public Task Current_history_missing_check_constraint_should_be_rejected()
        => AssertCorruptionRejectedAsync(
            "ALTER TABLE [dbo].[Taxes] DROP CONSTRAINT [CK_Taxes_Rate_0_100];");

    [Fact]
    public Task Current_history_wrong_default_constraint_should_be_rejected()
        => AssertCorruptionRejectedAsync("""
            DECLARE @constraintName sysname =
            (
                SELECT [default].[name]
                FROM [sys].[default_constraints] AS [default]
                INNER JOIN [sys].[columns] AS [column]
                    ON [column].[object_id] = [default].[parent_object_id]
                   AND [column].[column_id] =
                       [default].[parent_column_id]
                WHERE [default].[parent_object_id] =
                    OBJECT_ID(N'[dbo].[Stores]')
                  AND [column].[name] =
                    N'IsMultiLegalEntityEnabled'
            );

            DECLARE @dropSql nvarchar(max) =
                N'ALTER TABLE [dbo].[Stores] DROP CONSTRAINT '
                + QUOTENAME(@constraintName);

            EXEC [sys].[sp_executesql] @dropSql;

            ALTER TABLE [dbo].[Stores]
            ADD CONSTRAINT [DF_Acceptance_Stores_MultiLegal]
            DEFAULT (1) FOR [IsMultiLegalEntityEnabled];
            """);

    [Fact]
    public Task Current_history_extra_table_should_be_rejected()
        => AssertCorruptionRejectedAsync("""
            CREATE TABLE [dbo].[AcceptanceExtraTable]
            (
                [Id] int NOT NULL
                    CONSTRAINT [PK_AcceptanceExtraTable] PRIMARY KEY
            );
            """);

    [Fact]
    public async Task Exact_current_baseline_should_be_allowed()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using var db = database.CreateContext();
        await new EfCoreDatabaseMigrationExecutor(db).MigrateAsync();

        var result = await CreatePreflight(db).InspectAsync();

        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var expected = new EfCoreDatabaseSchemaManifestCatalog(db).GetCurrentManifest();
        await db.Database.OpenConnectionAsync();
        var actual = await new SqlServerSchemaSnapshotReader(db).ReadAsync(applied);
        await db.Database.CloseConnectionAsync();
        var expectedChecks = DatabaseSchemaCanonicalizer.CreateCategoryRecords(expected).All;
        var actualChecks = DatabaseSchemaCanonicalizer.CreateCategoryRecords(actual).All;
        var checkDifference = string.Join(" || ",
            expectedChecks.Except(actualChecks).Select(x => $"expected-only:{x}")
                .Concat(actualChecks.Except(expectedChecks).Select(x => $"actual-only:{x}")));
        result.IsAllowed.Should().BeTrue("the current schema must match; schema differences: {0}", checkDifference);
        result.State.Should().Be(DatabaseCompatibilityState.CurrentBaseline);
        result.SchemaMismatchCategoryCount.Should().Be(0);
    }

    [Fact]
    public async Task Current_baseline_rerun_should_be_no_op()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using var db = database.CreateContext();
        var migration = new EfCoreDatabaseMigrationExecutor(db);
        await migration.MigrateAsync();
        var historyBefore = await database.ReadMigrationHistoryAsync();
        var fingerprintBefore = await ReadFingerprintAsync(db);

        (await CreatePreflight(db).InspectAsync())
            .IsAllowed.Should().BeTrue();
        await migration.MigrateAsync();

        (await database.ReadMigrationHistoryAsync())
            .Should().Equal(historyBefore);
        (await ReadFingerprintAsync(db)).Should().Be(fingerprintBefore);
    }

    [Fact]
    public async Task Known_prefix_with_manifest_should_be_allowed()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using var db = database.CreateContext();
        await new EfCoreDatabaseMigrationExecutor(db).MigrateAsync();
        var sourceIds = db.Database.GetMigrations().ToList();
        var catalog = new EfCoreDatabaseSchemaManifestCatalog(db);

        catalog.TryGetManifestForAppliedMigrationPrefix(
                sourceIds,
                out var manifest)
            .Should().BeTrue();
        manifest.AppliedMigrationIds.Should().Equal(sourceIds);
        (await CreatePreflight(db).InspectAsync())
            .IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task Actual_baseline_prefix_manifest_should_exclude_inventory_idempotency_metadata()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using var db = database.CreateContext();
        var sourceIds = db.Database.GetMigrations().ToList();
        sourceIds.Should().Equal(
            BaselineMigrationId,
            InventoryPostingMigrationId,
            PurchaseReceiptAuditMigrationId,
            PurchaseReceiptCostPolicyMigrationId,
            InputInvoiceIdentityMigrationId,
            InputInvoiceSupplierResolutionMigrationId,
            InputInvoiceBuyerOwnerGuardMigrationId,
            InputInvoiceItemCatalogMappingMigrationId,
            InputInvoiceReconciliationMigrationId,
            InputInvoiceSingleActiveReceiptMigrationId,
            ReceivingWorkbenchMigrationId,
            ProvisionalReceivingItemsMigrationId, AcbPaymentsMigrationId, AcbCallbackInboxMigrationId,
            "20260908192844_AddAcbQrNotificationReconciliation", "20260909015242_AddPosQrInstallmentLinks", "20260909024821_AddAcbConfirmationAudit", "20260909055844_EnforceSingleDefaultBankAccount", "20260909061611_EnableSnapshotProfitReads", "20260909062834_AddAcbCallbackStoreRouting", "20260909080000_AddPosCollectionIdempotency", "20260909100000_AddSupplierBankFields",
            "20260909210000_AddPosOfflineJournal", "20260910002000_AddPosReceiptTemplates",
            "20260910012000_AddStoreReceiptIdentity", "20260910040620_AddProductLabelPrinting",
            "20260911053655_AddReceiptIntakePacking", "20260912120000_AddCustomerDisplayWifi",
            "20260912150000_AddReceivingPackagingPhoto",
            "20260914073514_AddOrderRewardEligibilitySnapshots",
            "20260914154923_AddPOSShiftCashReceipt",
            "20260919095814_AddCustomerReceivables",
            "20260919111155_AddCustomerDeposits",
            "20260919111529_AddDepositReturnRestoration",
            "20260919173000_MakePurchaseOrderSupplierOptional",
            "20260919174500_AllowPurchaseOrderVariantMultipleUnits",
           "20260920093000_OptimizeInventoryLedgerTimeline",
"20260921100000_AddInvoiceInputStockSupplementalMovements",
"20260923140000_AddInvoiceStockLegacyDocumentReferences",
"20260923160000_AddLegacyInvoiceImport",
"20260923180000_AddLegacyReturnArchive",
"20260924100000_AddAutoInvoiceIssuance",
"20260926113012_AddInvoiceIssuanceRoutingAndBuyerSelfService",
                "20260928160000_OptimizePosOrdersTimeline",
                "20260928200000_AddStoreReceiptDefault",
                "20260929090000_OptimizePosOrdersCount",
                "20260930100000_AddReceiptInvoiceFollowUp",
                "20260930150000_AddPOSCashAdjustmentRequests",
                "20260930160000_AllowSignedPOSExpectedCash",
                "20260930180000_AddStockReceiptEntryTerminal",
                "20260930190000_OptimizeInventoryLedgerProductSearch",
                "20260930200000_AddReceiptIntakeReviewDraft",
                "20260930210000_AddReceiptReviewPhoto",
                "20261001010000_AddKioskStations",
                "20261001072417_AddAdminMenuVisibility",
                "20261001080256_AddCustomerReceiptPrintPreference",
                "20261001115308_AddInputInvoiceLibrary",
                "20261001121734_PreserveInputInvoiceLibrarySource",
                "20261002233000_AddPurchaseReceiptPricingPlans",
                "20261003110000_AddPurchaseReceiptBillLines");
        var catalog = new EfCoreDatabaseSchemaManifestCatalog(db);

        catalog.TryGetManifestForAppliedMigrationPrefix(
                [BaselineMigrationId],
                out var baselineManifest)
            .Should().BeTrue();

        baselineManifest.AppliedMigrationIds.Should()
            .Equal(BaselineMigrationId);
        var baselineTransactions = baselineManifest.Tables
            .Should().ContainSingle(
                table => table.Identity.Name
                    == "inventorytransactions")
            .Which;
        baselineTransactions.Columns.Should().NotContain(
            column => column.Name == "idempotencykey");
        baselineTransactions.Indexes.Should().NotContain(
            index => index.Name
                == "ux_inventorytransactions_storeid_idempotencykey_active");

        catalog.TryGetManifestForAppliedMigrationPrefix(
                sourceIds,
                out var currentManifest)
            .Should().BeTrue();

        currentManifest.AppliedMigrationIds.Should().Equal(sourceIds);
        var currentTransactions = currentManifest.Tables
            .Should().ContainSingle(
                table => table.Identity.Name
                    == "inventorytransactions")
            .Which;
        currentTransactions.Columns.Should().ContainSingle(
            column => column.Name == "idempotencykey"
                && column.StoreType == "varbinary(32)"
                && column.IsNullable
                && !column.HasDefault);
        var currentIndex = currentTransactions.Indexes
            .Should().ContainSingle(
                index => index.Name
                    == "ux_inventorytransactions_storeid_idempotencykey_active"
                    && index.IsUnique)
            .Which;
        currentIndex.KeyColumns
            .Select(column => column.Name)
            .Should().Equal("storeid", "idempotencykey");
        currentIndex.Filter.Should()
            .Be("idempotencykeyisnotnullandisdeleted=0");

        var receiptAudit = currentManifest.Tables
            .Should().ContainSingle(
                table => table.Identity.Name
                    == "purchasereceiptauditevents")
            .Which;
        receiptAudit.Columns.Should().Contain(
            column => column.Name == "storeid" && !column.IsNullable);
        receiptAudit.Columns.Should().Contain(
            column => column.Name == "stockdocumentlineid" && column.IsNullable);
        receiptAudit.Columns.Should().Contain(
            column => column.Name == "occurredatutc" && !column.IsNullable);
        receiptAudit.Indexes.Should().ContainSingle(index =>
            index.Name ==
                "ix_purchasereceiptauditevents_store_document_occurred_id" &&
            index.KeyColumns.Select(column => column.Name).SequenceEqual(
                new[] { "storeid", "stockdocumentid", "occurredatutc", "id" }));
        receiptAudit.ForeignKeys.Should().HaveCount(4);
        receiptAudit.ForeignKeys.Should().OnlyContain(
            foreignKey => foreignKey.DeleteAction == "no_action");
    }

    [Fact]
    public async Task Valid_migration_prefix_without_manifest_should_be_rejected()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await database.CreateDatabaseAsync();
        await database.ExecuteAsync("""
            CREATE TABLE [dbo].[__EFMigrationsHistory]
            (
                [MigrationId] nvarchar(150) NOT NULL,
                [ProductVersion] nvarchar(32) NOT NULL,
                CONSTRAINT [PK___EFMigrationsHistory]
                    PRIMARY KEY ([MigrationId])
            );
            """);
        await using var db = database.CreateContext();

        var result = await CreatePreflight(db).InspectAsync();

        result.IsAllowed.Should().BeFalse();
        result.State.Should().Be(
            DatabaseCompatibilityState.UnsupportedMigrationPrefix);
        result.SafeReasonCode.Should().Be(
            "EmptyHistoryTableUnsupported");
    }

    private static async Task AssertCorruptionRejectedAsync(
        string corruptionSql,
        Action<DatabaseSchemaMismatchCounts>? assertMismatches = null,
        int? expectedMismatchCategoryCount = null,
        string? expectedChangedIndex = null)
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using var db = database.CreateContext();
        await new EfCoreDatabaseMigrationExecutor(db).MigrateAsync();
        var historyBefore = await database.ReadMigrationHistoryAsync();
        string? expectedIndexRecord = null;
        if (expectedChangedIndex is not null)
        {
            expectedIndexRecord = DatabaseSchemaCanonicalizer.CreateCategoryRecords(
                    new EfCoreDatabaseSchemaManifestCatalog(db).GetCurrentManifest())
                .Indexes.Single(record => record.Contains($"|{expectedChangedIndex}|", StringComparison.Ordinal));
            await db.Database.OpenConnectionAsync();
            var before = await new SqlServerSchemaSnapshotReader(db).ReadAsync(historyBefore);
            await db.Database.CloseConnectionAsync();
            DatabaseSchemaCanonicalizer.CreateCategoryRecords(before).Indexes
                .Should().Contain(expectedIndexRecord, "the target index must match before corruption");
        }
        await database.ExecuteAsync(corruptionSql);
        if (expectedIndexRecord is not null)
        {
            await db.Database.OpenConnectionAsync();
            var after = await new SqlServerSchemaSnapshotReader(db).ReadAsync(historyBefore);
            await db.Database.CloseConnectionAsync();
            DatabaseSchemaCanonicalizer.CreateCategoryRecords(after).Indexes
                .Should().NotContain(expectedIndexRecord, "the corruption must change the target index itself");
        }
        var corruptFingerprint = await ReadFingerprintAsync(db);
        var migration = new CountingMigrationExecutor();
        var mandatory = new CountingMandatorySeeder();
        var demo = new CountingDemoSeeder();
        var bootstrap = new CountingBootstrapper();
        var transaction = new CountingTransactionRunner();
        var pipeline = new MigrationExecutionPipeline(
            Options.Create(new SeedDataOptions()),
            Options.Create(new ProductionBootstrapOptions()),
            new TestHostEnvironment(),
            new MigratorConfigurationValidator(),
            CreatePreflight(db),
            migration,
            mandatory,
            demo,
            bootstrap,
            transaction);

        var action = () => pipeline.RunAsync(MigratorMode.SchemaOnly);

        var exception = await action.Should()
            .ThrowAsync<DatabaseCompatibilityException>();
        exception.Which.Result.State.Should().Be(
            DatabaseCompatibilityState.PartialOrCorruptBaseline);
        exception.Which.Result.SafeReasonCode.Should().Be(
            "StructuralSchemaMismatch");
        exception.Which.Result.SchemaMismatchCategoryCount
            .Should().BeGreaterThan(0);
        if (expectedMismatchCategoryCount is { } expectedCount)
        {
            exception.Which.Result.SchemaMismatchCategoryCount
                .Should().Be(expectedCount);
        }

        exception.Which.Result.SchemaMismatches.Should().NotBeNull();
        assertMismatches?.Invoke(
            exception.Which.Result.SchemaMismatches!);
        migration.Count.Should().Be(0);
        mandatory.Count.Should().Be(0);
        demo.Count.Should().Be(0);
        bootstrap.Count.Should().Be(0);
        transaction.Count.Should().Be(0);
        (await database.ReadMigrationHistoryAsync())
            .Should().Equal(historyBefore);
        (await ReadFingerprintAsync(db)).Should().Be(corruptFingerprint);
    }

    private static Task AssertOnlyColumnCorruptionRejectedAsync(
        string corruptionSql)
        => AssertCorruptionRejectedAsync(
            corruptionSql,
            mismatches =>
            {
                mismatches.Columns.Should().BeGreaterThan(0);
                mismatches.Tables.Should().Be(0);
                mismatches.PrimaryKeys.Should().Be(0);
                mismatches.ForeignKeys.Should().Be(0);
                mismatches.Indexes.Should().Be(0);
                mismatches.CheckConstraints.Should().Be(0);
                mismatches.Sequences.Should().Be(0);
            });

    private static Task AssertOnlyIndexCorruptionRejectedAsync(
        string corruptionSql)
        => AssertCorruptionRejectedAsync(
            corruptionSql,
            mismatches =>
            {
                mismatches.Indexes.Should().BeGreaterThan(0);
                mismatches.Tables.Should().Be(0);
                mismatches.Columns.Should().Be(0);
                mismatches.PrimaryKeys.Should().Be(0);
                mismatches.ForeignKeys.Should().Be(0);
                mismatches.CheckConstraints.Should().Be(0);
                mismatches.Sequences.Should().Be(0);
            });

    private static async Task<string> ReadFingerprintAsync(
        AppDbContext db)
    {
        var wasOpen =
            db.Database.GetDbConnection().State
            == System.Data.ConnectionState.Open;
        if (!wasOpen)
        {
            await db.Database.OpenConnectionAsync();
        }

        try
        {
            var migrationIds = (await db.Database
                    .GetAppliedMigrationsAsync())
                .ToList();
            var snapshot = await new SqlServerSchemaSnapshotReader(db)
                .ReadAsync(migrationIds);
            return snapshot.Fingerprint;
        }
        finally
        {
            if (!wasOpen)
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }

    private static SqlServerDatabaseBaselinePreflight CreatePreflight(
        AppDbContext db)
        => new(
            db,
            new EfCoreDatabaseMigrationCatalog(db),
            new SqlServerDatabaseObjectInventoryReader(db),
            new EfCoreDatabaseSchemaManifestCatalog(db),
            new SqlServerSchemaSnapshotReader(db));

    private sealed class CountingMigrationExecutor
        : IDatabaseMigrationExecutor
    {
        public int Count { get; private set; }

        public Task MigrateAsync(CancellationToken ct = default)
        {
            Count++;
            return Task.CompletedTask;
        }
    }

    private sealed class CountingMandatorySeeder
        : IMandatorySecuritySeeder
    {
        public int Count { get; private set; }

        public Task SeedAsync(CancellationToken ct = default)
        {
            Count++;
            return Task.CompletedTask;
        }
    }

    private sealed class CountingDemoSeeder : IDemoDataSeeder
    {
        public int Count { get; private set; }

        public Task SeedAsync(
            SeedDataOptions options,
            CancellationToken ct = default)
        {
            Count++;
            return Task.CompletedTask;
        }
    }

    private sealed class CountingBootstrapper : IProductionBootstrapper
    {
        public int Count { get; private set; }

        public Task<ProductionBootstrapPlan> InspectAsync(
            CancellationToken ct = default)
        {
            Count++;
            return Task.FromResult(new ProductionBootstrapPlan(
                ProductionBootstrapPlanState.Empty,
                RequiresChanges: true));
        }

        public Task<ProductionBootstrapResult> ApplyAsync(
            ProductionBootstrapPlan plan,
            CancellationToken ct = default)
        {
            Count++;
            return Task.FromResult(ProductionBootstrapResult.Created);
        }
    }

    private sealed class CountingTransactionRunner
        : IProvisioningTransactionRunner
    {
        public int Count { get; private set; }

        public Task ExecuteAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken ct = default)
        {
            Count++;
            return operation(ct);
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } =
            Environments.Production;
        public string ApplicationName { get; set; } = "GaoApp.Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
