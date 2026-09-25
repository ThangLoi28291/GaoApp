using GaoApp.Application.Common.Options;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Migrations;
using GaoApp.Infrastructure.Data.Seed;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Configuration;

[Collection("R1FinalDatabasePreflight")]
public sealed class DatabaseSecurityMetadataTests
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
    public Task Current_baseline_with_application_database_user_should_be_allowed()
        => AssertCurrentBaselineSecurityMetadataAllowedAsync(
            """
            CREATE USER [R1FinalApplicationUser] WITHOUT LOGIN;
            """,
            ["R1FinalApplicationUser"],
            counts =>
            {
                counts.DatabaseUsers.Should().Be(1);
                counts.CustomDatabaseRoles.Should().Be(0);
            });

    [Fact]
    public Task Current_baseline_with_custom_runtime_role_should_be_allowed()
        => AssertCurrentBaselineSecurityMetadataAllowedAsync(
            """
            CREATE ROLE [R1FinalRuntimeRole];
            """,
            ["R1FinalRuntimeRole"],
            counts =>
            {
                counts.DatabaseUsers.Should().Be(0);
                counts.CustomDatabaseRoles.Should().Be(1);
            });

    [Fact]
    public Task Current_baseline_with_user_and_role_membership_should_be_allowed()
        => AssertCurrentBaselineSecurityMetadataAllowedAsync(
            """
            CREATE USER [R1FinalRoleMember] WITHOUT LOGIN;
            CREATE ROLE [R1FinalMemberRole];
            ALTER ROLE [R1FinalMemberRole]
                ADD MEMBER [R1FinalRoleMember];
            """,
            ["R1FinalRoleMember", "R1FinalMemberRole"],
            counts =>
            {
                counts.DatabaseUsers.Should().Be(1);
                counts.CustomDatabaseRoles.Should().Be(1);
                counts.RoleMemberships.Should().Be(1);
            });

    [Fact]
    public Task Current_baseline_with_certificate_or_key_should_be_allowed()
        => AssertCurrentBaselineSecurityMetadataAllowedAsync(
            """
            CREATE MASTER KEY
                ENCRYPTION BY PASSWORD =
                    'Synthetic-R1Final-Master-Key-42!';
            CREATE CERTIFICATE [R1FinalRuntimeCertificate]
                WITH SUBJECT = N'R1 Final runtime certificate';
            CREATE SYMMETRIC KEY [R1FinalRuntimeSymmetricKey]
                WITH ALGORITHM = AES_256
                ENCRYPTION BY CERTIFICATE
                    [R1FinalRuntimeCertificate];
            """,
            [
                "R1FinalRuntimeCertificate",
                "R1FinalRuntimeSymmetricKey"
            ],
            counts =>
            {
                counts.Certificates.Should().Be(1);
                counts.SymmetricKeys.Should().Be(1);
            });

    [Fact]
    public Task Current_baseline_with_database_scoped_credential_should_be_allowed()
        => AssertCurrentBaselineSecurityMetadataAllowedAsync(
            """
            CREATE MASTER KEY
                ENCRYPTION BY PASSWORD =
                    'Synthetic-R1Final-Credential-Master-Key-42!';
            CREATE DATABASE SCOPED CREDENTIAL
                [R1FinalDeploymentCredential]
            WITH IDENTITY = N'R1FinalDeploymentIdentity',
                 SECRET = N'Synthetic-R1Final-Credential-42!';
            """,
            [
                "R1FinalDeploymentCredential",
                "R1FinalDeploymentIdentity"
            ],
            counts =>
                counts.DatabaseScopedCredentials.Should().Be(1));

    [Fact]
    public async Task Existing_empty_database_with_deployment_principal_should_be_allowed()
    {
        const string userName = "R1FinalDeploymentPrincipal";
        const string roleName = "R1FinalDeploymentRole";
        await using var database = new PreflightAcceptanceDatabase();
        await database.CreateDatabaseAsync();
        await database.ExecuteAsync(
            $"""
             CREATE USER [{userName}] WITHOUT LOGIN;
             CREATE ROLE [{roleName}];
             ALTER ROLE [{roleName}] ADD MEMBER [{userName}];
             """);
        await using var db = database.CreateContext();
        var preflight = CreatePreflight(db);

        var beforeMigration = await preflight.InspectAsync();

        beforeMigration.IsAllowed.Should().BeTrue();
        beforeMigration.State.Should().Be(
            DatabaseCompatibilityState.ExistingEmpty);
        beforeMigration.StructuralObjectCount.Should().Be(0);
        beforeMigration.SecurityMetadataCounts.Should().NotBeNull();
        beforeMigration.SecurityMetadataCounts!.DatabaseUsers
            .Should().Be(1);
        beforeMigration.SecurityMetadataCounts.CustomDatabaseRoles
            .Should().Be(1);
        beforeMigration.SecurityMetadataCounts.RoleMemberships
            .Should().Be(1);

        var execution = CreateExecutionHarness(db, preflight);
        string provisioningAfterFirst = string.Empty;
        IReadOnlyList<string> historyAfterFirst = [];
        var output = await CaptureConsoleAsync(async () =>
        {
            await execution.Pipeline.RunAsync(MigratorMode.SchemaOnly);
            provisioningAfterFirst =
                await database.ReadProvisioningStateSignatureAsync();
            historyAfterFirst =
                await database.ReadMigrationHistoryAsync();
            await execution.Pipeline.RunAsync(MigratorMode.SchemaOnly);
        });

        var afterMigration = await preflight.InspectAsync();
        afterMigration.IsAllowed.Should().BeTrue();
        afterMigration.State.Should().Be(
            DatabaseCompatibilityState.CurrentBaseline);
        (await database.ReadProvisioningStateSignatureAsync())
            .Should().Be(provisioningAfterFirst);
        (await database.ReadMigrationHistoryAsync())
            .Should().Equal(historyAfterFirst);
        AssertCurrentMigrationHistory(historyAfterFirst);
        AssertTwoNoOpSafeRuns(execution);
        AssertNamesAbsent(output, userName, roleName);
    }

    [Fact]
    public async Task Security_metadata_should_not_change_structural_fingerprint()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using var db = database.CreateContext();
        await new EfCoreDatabaseMigrationExecutor(db).MigrateAsync();
        var fingerprintBefore = await ReadFingerprintAsync(db);

        await database.ExecuteAsync(
            """
            CREATE USER [R1FinalFingerprintUser] WITHOUT LOGIN;
            CREATE ROLE [R1FinalFingerprintRole];
            ALTER ROLE [R1FinalFingerprintRole]
                ADD MEMBER [R1FinalFingerprintUser];
            CREATE CERTIFICATE [R1FinalFingerprintCertificate]
                ENCRYPTION BY PASSWORD =
                    'Synthetic-R1Final-Certificate-42!'
                WITH SUBJECT = N'R1 Final fingerprint certificate';
            """);

        var fingerprintAfter = await ReadFingerprintAsync(db);
        var compatibility = await CreatePreflight(db).InspectAsync();

        fingerprintAfter.Should().Be(fingerprintBefore);
        compatibility.IsAllowed.Should().BeTrue();
        compatibility.State.Should().Be(
            DatabaseCompatibilityState.CurrentBaseline);
    }

    [Fact]
    public async Task Security_metadata_names_should_not_appear_in_logs()
    {
        string[] names =
        [
            "R1FinalLogUser",
            "R1FinalLogRole",
            "R1FinalLogCertificate",
            "R1FinalLogCredential",
            "R1FinalLogCredentialIdentity"
        ];
        await using var database = new PreflightAcceptanceDatabase();
        await using var db = database.CreateContext();
        await new EfCoreDatabaseMigrationExecutor(db).MigrateAsync();
        await database.ExecuteAsync(
            """
            CREATE USER [R1FinalLogUser] WITHOUT LOGIN;
            CREATE ROLE [R1FinalLogRole];
            ALTER ROLE [R1FinalLogRole]
                ADD MEMBER [R1FinalLogUser];
            CREATE MASTER KEY
                ENCRYPTION BY PASSWORD =
                    'Synthetic-R1Final-Log-Master-Key-42!';
            CREATE CERTIFICATE [R1FinalLogCertificate]
                WITH SUBJECT = N'R1 Final log certificate';
            CREATE DATABASE SCOPED CREDENTIAL
                [R1FinalLogCredential]
            WITH IDENTITY = N'R1FinalLogCredentialIdentity',
                 SECRET = N'Synthetic-R1Final-Log-Credential-42!';
            """);
        var preflight = CreatePreflight(db);
        var execution = CreateExecutionHarness(db, preflight);

        var output = await CaptureConsoleAsync(
            () => execution.Pipeline.RunAsync(MigratorMode.SchemaOnly));

        output.Should().Contain(
            "Database security metadata audit:");
        output.Should().Contain("Users=1");
        output.Should().Contain("CustomRoles=1");
        output.Should().Contain("RoleMemberships=1");
        output.Should().Contain("Certificates=1");
        output.Should().Contain("DatabaseScopedCredentials=1");
        AssertNamesAbsent(output, names);
    }

    [Fact]
    public Task Current_baseline_with_foreign_table_should_still_be_rejected()
        => AssertForeignStructuralObjectRejectedAsync(
            """
            CREATE TABLE [dbo].[R1FinalForeignTable]
            (
                [Id] int NOT NULL
                    CONSTRAINT [PK_R1FinalForeignTable]
                    PRIMARY KEY
             );
            """);

    [Fact]
    public Task Current_baseline_with_foreign_view_should_still_be_rejected()
        => AssertForeignStructuralObjectRejectedAsync(
            """
            CREATE VIEW [dbo].[R1FinalForeignView]
            AS SELECT CAST(1 AS int) AS [Value];
            """);

    private static async Task
        AssertCurrentBaselineSecurityMetadataAllowedAsync(
            string securityMetadataSql,
            IReadOnlyList<string> securityMetadataNames,
            Action<DatabaseSecurityMetadataCounts> assertCounts)
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using var db = database.CreateContext();
        await new EfCoreDatabaseMigrationExecutor(db).MigrateAsync();
        await database.ExecuteAsync(securityMetadataSql);
        var preflight = CreatePreflight(db);

        var compatibility = await preflight.InspectAsync();

        compatibility.IsAllowed.Should().BeTrue();
        compatibility.State.Should().Be(
            DatabaseCompatibilityState.CurrentBaseline);
        compatibility.SecurityMetadataCounts.Should().NotBeNull();
        assertCounts(compatibility.SecurityMetadataCounts!);

        var execution = CreateExecutionHarness(db, preflight);
        string provisioningAfterFirst = string.Empty;
        IReadOnlyList<string> historyAfterFirst = [];
        var output = await CaptureConsoleAsync(async () =>
        {
            await execution.Pipeline.RunAsync(MigratorMode.SchemaOnly);
            provisioningAfterFirst =
                await database.ReadProvisioningStateSignatureAsync();
            historyAfterFirst =
                await database.ReadMigrationHistoryAsync();
            await execution.Pipeline.RunAsync(MigratorMode.SchemaOnly);
        });

        (await database.ReadProvisioningStateSignatureAsync())
            .Should().Be(provisioningAfterFirst);
        (await database.ReadMigrationHistoryAsync())
            .Should().Equal(historyAfterFirst);
        AssertCurrentMigrationHistory(historyAfterFirst);
        AssertTwoNoOpSafeRuns(execution);
        AssertNamesAbsent(output, securityMetadataNames);
    }

    private static async Task AssertForeignStructuralObjectRejectedAsync(
        string structuralObjectSql)
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using var db = database.CreateContext();
        await new EfCoreDatabaseMigrationExecutor(db).MigrateAsync();
        await database.ExecuteAsync(structuralObjectSql);
        var signatureBefore =
            await database.ReadDatabaseObjectSignatureAsync();
        var preflight = CreatePreflight(db);
        var compatibility = await preflight.InspectAsync();
        var migration = new CountingMigrationExecutor(
            new EfCoreDatabaseMigrationExecutor(db));
        var mandatory = new CountingMandatorySeeder(
            new MandatorySecuritySeeder(db));
        var demo = new CountingDemoSeeder();
        var bootstrap = new CountingBootstrapper();
        var transaction = new CountingTransactionRunner(
            new EfCoreProvisioningTransactionRunner(db));
        var pipeline = new MigrationExecutionPipeline(
            Options.Create(new SeedDataOptions()),
            Options.Create(new ProductionBootstrapOptions()),
            new TestHostEnvironment(),
            new MigratorConfigurationValidator(),
            preflight,
            migration,
            mandatory,
            demo,
            bootstrap,
            transaction);

        var action = () => pipeline.RunAsync(MigratorMode.SchemaOnly);

        compatibility.IsAllowed.Should().BeFalse();
        await action.Should()
            .ThrowAsync<DatabaseCompatibilityException>();
        migration.Count.Should().Be(0);
        mandatory.Count.Should().Be(0);
        demo.Count.Should().Be(0);
        bootstrap.InspectCount.Should().Be(0);
        bootstrap.ApplyCount.Should().Be(0);
        transaction.Count.Should().Be(0);
        (await database.ReadDatabaseObjectSignatureAsync())
            .Should().Be(signatureBefore);
        AssertCurrentMigrationHistory(
            await database.ReadMigrationHistoryAsync());
    }

    private static SqlServerDatabaseBaselinePreflight CreatePreflight(
        AppDbContext db)
        => new(
            db,
            new EfCoreDatabaseMigrationCatalog(db),
            new SqlServerDatabaseObjectInventoryReader(db),
            new EfCoreDatabaseSchemaManifestCatalog(db),
            new SqlServerSchemaSnapshotReader(db));

    private static void AssertCurrentMigrationHistory(
        IReadOnlyList<string> migrationIds)
    {
        migrationIds.Should().BeEquivalentTo(
            [
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
                ProvisionalReceivingItemsMigrationId,
                AcbPaymentsMigrationId,
                AcbCallbackInboxMigrationId,
                "20260908192844_AddAcbQrNotificationReconciliation", "20260909015242_AddPosQrInstallmentLinks",
                "20260909024821_AddAcbConfirmationAudit", "20260909055844_EnforceSingleDefaultBankAccount", "20260909061611_EnableSnapshotProfitReads", "20260909062834_AddAcbCallbackStoreRouting", "20260909080000_AddPosCollectionIdempotency", "20260909100000_AddSupplierBankFields", "20260909210000_AddPosOfflineJournal", "20260910002000_AddPosReceiptTemplates", "20260910012000_AddStoreReceiptIdentity", "20260910040620_AddProductLabelPrinting", "20260911053655_AddReceiptIntakePacking",
                "20260912120000_AddCustomerDisplayWifi",
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
"20260924100000_AddAutoInvoiceIssuance"
            ]);
    }

    private static ExecutionHarness CreateExecutionHarness(
        AppDbContext db,
        IDatabaseBaselinePreflight preflight)
    {
        var migration = new CountingMigrationExecutor(
            new EfCoreDatabaseMigrationExecutor(db));
        var mandatory = new CountingMandatorySeeder(
            new MandatorySecuritySeeder(db));
        var demo = new CountingDemoSeeder();
        var bootstrap = new CountingBootstrapper();
        var transaction = new CountingTransactionRunner(
            new EfCoreProvisioningTransactionRunner(db));
        var pipeline = new MigrationExecutionPipeline(
            Options.Create(new SeedDataOptions()),
            Options.Create(new ProductionBootstrapOptions()),
            new TestHostEnvironment(),
            new MigratorConfigurationValidator(),
            preflight,
            migration,
            mandatory,
            demo,
            bootstrap,
            transaction);
        return new ExecutionHarness(
            pipeline,
            migration,
            mandatory,
            demo,
            bootstrap,
            transaction);
    }

    private static void AssertTwoNoOpSafeRuns(
        ExecutionHarness execution)
    {
        execution.Migration.Count.Should().Be(2);
        execution.Mandatory.Count.Should().Be(0);
        execution.Demo.Count.Should().Be(0);
        execution.Bootstrap.InspectCount.Should().Be(0);
        execution.Bootstrap.ApplyCount.Should().Be(0);
        execution.Transaction.Count.Should().Be(0);
    }

    private static async Task<string> ReadFingerprintAsync(
        AppDbContext db)
    {
        await db.Database.OpenConnectionAsync();

        try
        {
            var migrationIds = (await db.Database
                    .GetAppliedMigrationsAsync())
                .ToList();
            var snapshot =
                await new SqlServerSchemaSnapshotReader(db)
                    .ReadAsync(migrationIds);
            return snapshot.Fingerprint;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task<string> CaptureConsoleAsync(
        Func<Task> action)
    {
        var original = Console.Out;
        using var output = new StringWriter();
        Console.SetOut(output);

        try
        {
            await action();
            return output.ToString();
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private static void AssertNamesAbsent(
        string output,
        params string[] names)
        => AssertNamesAbsent(output, (IReadOnlyList<string>)names);

    private static void AssertNamesAbsent(
        string output,
        IReadOnlyList<string> names)
    {
        foreach (var name in names)
        {
            output.Should().NotContain(name);
        }
    }

    private sealed record ExecutionHarness(
        MigrationExecutionPipeline Pipeline,
        CountingMigrationExecutor Migration,
        CountingMandatorySeeder Mandatory,
        CountingDemoSeeder Demo,
        CountingBootstrapper Bootstrap,
        CountingTransactionRunner Transaction);

    private sealed class CountingMigrationExecutor(
        IDatabaseMigrationExecutor inner)
        : IDatabaseMigrationExecutor
    {
        public int Count { get; private set; }

        public async Task MigrateAsync(
            CancellationToken ct = default)
        {
            Count++;
            await inner.MigrateAsync(ct);
        }
    }

    private sealed class CountingMandatorySeeder(
        IMandatorySecuritySeeder inner)
        : IMandatorySecuritySeeder
    {
        public int Count { get; private set; }

        public async Task SeedAsync(
            CancellationToken ct = default)
        {
            Count++;
            await inner.SeedAsync(ct);
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

    private sealed class CountingBootstrapper
        : IProductionBootstrapper
    {
        public int InspectCount { get; private set; }
        public int ApplyCount { get; private set; }

        public Task<ProductionBootstrapPlan> InspectAsync(
            CancellationToken ct = default)
        {
            InspectCount++;
            return Task.FromResult(new ProductionBootstrapPlan(
                ProductionBootstrapPlanState.Disabled,
                RequiresChanges: false));
        }

        public Task<ProductionBootstrapResult> ApplyAsync(
            ProductionBootstrapPlan plan,
            CancellationToken ct = default)
        {
            ApplyCount++;
            return Task.FromResult(
                ProductionBootstrapResult.AlreadyProvisioned);
        }
    }

    private sealed class CountingTransactionRunner(
        IProvisioningTransactionRunner inner)
        : IProvisioningTransactionRunner
    {
        public int Count { get; private set; }

        public async Task ExecuteAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken ct = default)
        {
            Count++;
            await inner.ExecuteAsync(operation, ct);
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } =
            Environments.Production;
        public string ApplicationName { get; set; } =
            "GaoApp.Tests";
        public string ContentRootPath { get; set; } =
            string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
