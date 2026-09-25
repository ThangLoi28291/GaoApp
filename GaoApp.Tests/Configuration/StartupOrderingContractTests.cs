using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Services.Inventory;
using GaoApp.Infrastructure;
using GaoApp.Infrastructure.Services.Invoices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GaoApp.Tests.Configuration;

public sealed class StartupOrderingContractTests
{
    [Fact]
    public void Normal_web_startup_in_every_environment_has_no_database_initialization()
    {
        var source = ReadRepositoryFile("GaoApp.Web", "Program.cs");
        var normal = source[source.IndexOf("await app.ValidateStartupAsync();", StringComparison.Ordinal)..];
        foreach (var forbidden in new[] { "MigrateAsync", "MigrateAndSeed", "SeedAsync", "SeedData.", "Bootstrapper", "ExecuteSql" })
            Assert.DoesNotContain(forbidden, normal, StringComparison.Ordinal);
        var extension = ReadRepositoryFile("GaoApp.Web", "Configuration", "DatabaseStartupExtensions.cs");
        Assert.DoesNotContain("AppDbContext", extension, StringComparison.Ordinal);
    }

    [Fact]
    public void Menu_recovery_runner_parses_and_removes_only_the_exact_flag_before_builder()
    {
        var source = ReadRepositoryFile("GaoApp.Web", "Program.cs");

        var flagIndex = source.IndexOf(
            "const string recoverAdminMenusOnlyFlag = \"--recover-admin-menus-only\";",
            StringComparison.Ordinal);
        var countIndex = source.IndexOf(
            "var recoveryFlagCount = args.Count(",
            StringComparison.Ordinal);
        var duplicateIndex = source.IndexOf(
            "if (recoveryFlagCount > 1)",
            StringComparison.Ordinal);
        var builderArgsIndex = source.IndexOf(
            "var builderArgs = recoverAdminMenusOnly",
            StringComparison.Ordinal);
        var builderIndex = source.IndexOf(
            "WebApplication.CreateBuilder(builderArgs)",
            StringComparison.Ordinal);

        Assert.True(
            flagIndex >= 0
            && flagIndex < countIndex
            && countIndex < duplicateIndex
            && duplicateIndex < builderArgsIndex
            && builderArgsIndex < builderIndex,
            "The exact recovery flag must be counted, rejected when duplicated, removed, and only then passed to the Web builder.");

        var parsingBlock = source[flagIndex..builderIndex];
        Assert.Contains("StringComparison.Ordinal", parsingBlock, StringComparison.Ordinal);
        Assert.Contains("var recoverAdminMenusOnly = recoveryFlagCount == 1;", parsingBlock, StringComparison.Ordinal);
        Assert.Contains("args.Where(", parsingBlock, StringComparison.Ordinal);
        Assert.Contains("!string.Equals(", parsingBlock, StringComparison.Ordinal);
        Assert.Contains(": args;", parsingBlock, StringComparison.Ordinal);
        Assert.Contains("Environment.ExitCode = 2;", parsingBlock, StringComparison.Ordinal);
        Assert.Contains("return;", parsingBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Menu_recovery_runner_branches_after_build_and_bypasses_all_other_startup_work()
    {
        var source = ReadRepositoryFile("GaoApp.Web", "Program.cs");

        var buildIndex = source.IndexOf("var app = builder.Build();", StringComparison.Ordinal);
        var branchIndex = source.IndexOf("if (recoverAdminMenusOnly)", StringComparison.Ordinal);
        var validationIndex = source.IndexOf("await app.ValidateStartupAsync();", StringComparison.Ordinal);
        var middlewareIndex = source.IndexOf("app.UseMiddleware<GlobalExceptionMiddleware>();", StringComparison.Ordinal);
        var runIndex = source.LastIndexOf("app.Run();", StringComparison.Ordinal);

        Assert.True(
            buildIndex >= 0
            && buildIndex < branchIndex
            && branchIndex < validationIndex
            && validationIndex < middlewareIndex
            && middlewareIndex < runIndex,
            "Recovery must branch immediately after builder.Build() and before validation, migration, middleware, routes, and app.Run().");

        var recoveryBranch = source[branchIndex..validationIndex];
        Assert.Contains("app.Services.CreateScope()", recoveryBranch, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<AppDbContext>()", recoveryBranch, StringComparison.Ordinal);
        Assert.Contains("db.Database.GetDbConnection()", recoveryBranch, StringComparison.Ordinal);
        Assert.Contains("db.Stores", recoveryBranch, StringComparison.Ordinal);
        Assert.Contains("IgnoreQueryFilters()", recoveryBranch, StringComparison.Ordinal);
        Assert.Contains("store.IsActive && !store.IsDeleted", recoveryBranch, StringComparison.Ordinal);
        Assert.Equal(
            1,
            CountOccurrences(recoveryBranch, "AdminMenuSeeder.SeedAsync(db, cancellation.Token)"));
        Assert.Contains("return;", recoveryBranch, StringComparison.Ordinal);

        var forbiddenCalls = new[]
        {
            "MigrateAndSeedDatabaseAsync",
            "MigrateAsync",
            "MandatorySecuritySeeder",
            "SeedPermissionsAsync",
            "SeedLegalEntityAdminMenusAsync",
            "SeedPurchaseAdminMenusAsync",
            "SeedUsersAsync",
            "ProductionBootstrapper",
            "UseMiddleware",
            "MapController",
            "app.Run"
        };

        foreach (var forbiddenCall in forbiddenCalls)
        {
            Assert.DoesNotContain(forbiddenCall, recoveryBranch, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Menu_recovery_runner_has_bounded_output_cancellation_and_exit_codes()
    {
        var source = ReadRepositoryFile("GaoApp.Web", "Program.cs");
        var branchIndex = source.IndexOf("if (recoverAdminMenusOnly)", StringComparison.Ordinal);
        var validationIndex = source.IndexOf("await app.ValidateStartupAsync();", StringComparison.Ordinal);

        Assert.True(branchIndex >= 0 && validationIndex > branchIndex);
        var recoveryBranch = source[branchIndex..validationIndex];

        Assert.Contains("Console.CancelKeyPress += cancelHandler;", recoveryBranch, StringComparison.Ordinal);
        Assert.Contains("cancellation.Cancel();", recoveryBranch, StringComparison.Ordinal);
        Assert.Contains("Console.CancelKeyPress -= cancelHandler;", recoveryBranch, StringComparison.Ordinal);
        Assert.Contains("app.Environment.EnvironmentName", recoveryBranch, StringComparison.Ordinal);
        Assert.Contains("connection.DataSource", recoveryBranch, StringComparison.Ordinal);
        Assert.Contains("connection.Database", recoveryBranch, StringComparison.Ordinal);
        Assert.Contains("activeStoreCount", recoveryBranch, StringComparison.Ordinal);
        Assert.Contains("Environment.ExitCode = 0;", recoveryBranch, StringComparison.Ordinal);
        Assert.Contains("Environment.ExitCode = 1;", recoveryBranch, StringComparison.Ordinal);
        Assert.Contains("catch (OperationCanceledException)", recoveryBranch, StringComparison.Ordinal);
        Assert.Contains("catch (Exception ex)", recoveryBranch, StringComparison.Ordinal);
        Assert.Contains("ex.GetType().Name", recoveryBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("GetConnectionString", recoveryBranch, StringComparison.Ordinal);
        Assert.DoesNotContain(".ConnectionString", recoveryBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("ex.Message", recoveryBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", recoveryBranch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Secret", recoveryBranch, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Mandatory_seed_reconciles_permissions_then_core_menu_then_additive_menu_sets()
    {
        var source = ReadRepositoryFile(
            "GaoApp.Infrastructure",
            "Data",
            "Seed",
            "MigrationSeedRunners.cs");

        var permissionsIndex = source.IndexOf(
            "SecuritySeedData.SeedPermissionsAsync(_db, ct)",
            StringComparison.Ordinal);
        var coreMenuIndex = source.IndexOf(
            "AdminMenuSeeder.SeedAsync(_db, ct)",
            StringComparison.Ordinal);
        var legalMenuIndex = source.IndexOf(
            "SecuritySeedData.SeedLegalEntityAdminMenusAsync(_db, ct)",
            StringComparison.Ordinal);
        var purchaseMenuIndex = source.IndexOf(
            "SecuritySeedData.SeedPurchaseAdminMenusAsync(_db, ct)",
            StringComparison.Ordinal);

        Assert.True(
            permissionsIndex >= 0 &&
            permissionsIndex < coreMenuIndex &&
            coreMenuIndex < legalMenuIndex &&
            legalMenuIndex < purchaseMenuIndex,
            "Required order: permissions, canonical core menu recovery, additive legal menu, additive purchase menu.");
    }

    [Fact]
    public void WebStartup_RethrowsStartupExceptions()
    {
        var source = ReadRepositoryFile("GaoApp.Web", "Program.cs");
        var catchIndex = source.LastIndexOf(
            "catch (Exception ex)",
            StringComparison.Ordinal);
        var finallyIndex = source.LastIndexOf(
            "finally",
            StringComparison.Ordinal);

        Assert.True(catchIndex >= 0 && finallyIndex > catchIndex);
        var catchBlock = source[catchIndex..finallyIndex];
        Assert.Contains("throw;", catchBlock, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Environment.ExitCode = 1",
            catchBlock,
            StringComparison.Ordinal);
    }

    [Fact]
    public void StartupValidation_HasOneEntryPointAndNoHostedRegistration()
    {
        var program = ReadRepositoryFile("GaoApp.Web", "Program.cs");
        var registration = ReadRepositoryFile(
            "GaoApp.Web",
            "Configuration",
            "ServiceCollectionExtensions.cs");

        Assert.Equal(
            1,
            CountOccurrences(program, "await app.ValidateStartupAsync();"));
        Assert.DoesNotContain(
            "AddHostedService<StartupValidationHostedService>",
            registration,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Migrator_ValidatesSeedOptionsBeforeDatabaseMigration()
    {
        var source = ReadRepositoryFile(
            "GaoApp.Infrastructure",
            "Data",
            "Migrations",
            "MigrationExecutionPipeline.cs");

        var validationIndex = source.IndexOf(
            "_configurationValidator.ValidateMode(",
            StringComparison.Ordinal);
        var preflightIndex = source.IndexOf(
            "_databasePreflight.InspectAsync(ct)",
            StringComparison.Ordinal);
        var migrationIndex = source.IndexOf(
            "_migrationExecutor.MigrateAsync(ct)",
            StringComparison.Ordinal);

        Assert.True(validationIndex >= 0, "Seed option validation was not found.");
        Assert.True(preflightIndex >= 0, "Database preflight was not found.");
        Assert.True(migrationIndex >= 0, "Database migration call was not found.");
        Assert.True(
            validationIndex < preflightIndex
            && preflightIndex < migrationIndex,
            "Configuration validation and database preflight must run before database migration.");
    }

    [Fact]
    public void Migrator_DI_registration_resolves_input_invoice_document_library_without_database_access()
    {
        var source = ReadRepositoryFile("GaoApp.Migrator", "Program.cs");
        var infrastructureIndex = source.IndexOf(
            "builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);",
            StringComparison.Ordinal);
        var buildIndex = source.IndexOf(
            "var host = builder.Build();",
            StringComparison.Ordinal);

        Assert.True(
            infrastructureIndex >= 0 && buildIndex > infrastructureIndex,
            "Migrator infrastructure registration must precede host construction.");

        var registrationBlock = source[infrastructureIndex..buildIndex];
        Assert.Contains(
            "builder.Services.AddSingleton<",
            registrationBlock,
            StringComparison.Ordinal);
        Assert.Contains(
            "IInputInvoiceXmlDocumentParser,",
            registrationBlock,
            StringComparison.Ordinal);
        Assert.Contains(
            "InputInvoiceXmlDocumentParser>();",
            registrationBlock,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".AddApplication(",
            source,
            StringComparison.Ordinal);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Server=127.0.0.1,1;Database=MigratorDiContract;" +
                    "Integrated Security=True;TrustServerCertificate=True;" +
                    "Connect Timeout=1"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        services.AddSingleton<
            IInputInvoiceXmlDocumentParser,
            InputInvoiceXmlDocumentParser>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var library = scope.ServiceProvider
            .GetRequiredService<IInputInvoiceDocumentLibrary>();

        Assert.IsType<FileSystemInputInvoiceDocumentLibrary>(library);
    }

    [Fact]
    public void DatabasePreflight_UsesCompleteInventoryAndStructuralManifest()
    {
        var preflight = ReadRepositoryFile(
            "GaoApp.Infrastructure",
            "Data",
            "Migrations",
            "SqlServerDatabaseBaselinePreflight.cs");
        var inventory = ReadRepositoryFile(
            "GaoApp.Infrastructure",
            "Data",
            "Migrations",
            "SqlServerDatabaseObjectInventoryReader.cs");

        Assert.Contains(
            "_inventoryReader.ReadAsync(ct)",
            preflight,
            StringComparison.Ordinal);
        Assert.Contains(
            "DatabaseSchemaComparer.Compare(",
            preflight,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "GetExpected" + "Tables",
            preflight,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "CurrentApplication" + "Tables",
            preflight,
            StringComparison.Ordinal);
        Assert.Contains("[sys].[tables]", inventory);
        Assert.Contains("[sys].[views]", inventory);
        Assert.Contains("[sys].[procedures]", inventory);
        Assert.Contains("[sys].[sequences]", inventory);
        Assert.Contains("[sys].[synonyms]", inventory);
        Assert.Contains("[sys].[schemas]", inventory);
        Assert.Contains("[sys].[types]", inventory);
    }

    [Fact]
    public void Bootstrap_is_checked_again_inside_atomic_transaction_before_apply()
    {
        var source = ReadRepositoryFile("GaoApp.Infrastructure", "Data", "Migrations", "MigrationExecutionPipeline.cs");
        var inspect = source.IndexOf("_productionBootstrapper.InspectAsync(ct)", StringComparison.Ordinal);
        var transaction = source.IndexOf("_transactionRunner.ExecuteAsync(", StringComparison.Ordinal);
        var locked = source.IndexOf("_productionBootstrapper.InspectAsync(transactionCt)", StringComparison.Ordinal);
        var apply = source.IndexOf("_productionBootstrapper.ApplyAsync(", StringComparison.Ordinal);
        Assert.True(inspect >= 0 && inspect < transaction && transaction < locked && locked < apply);
        Assert.DoesNotContain("_mandatorySecuritySeeder", source[locked..apply], StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionBootstrapper_DoesNotOwnAnIndependentTransaction()
    {
        var source = ReadRepositoryFile(
            "GaoApp.Infrastructure",
            "Data",
            "Seed",
            "ProductionBootstrapper.cs");

        Assert.DoesNotContain(
            "BeginTransaction",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "Database.CurrentTransaction is null",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TenantValidation_UsesOriginalValuesAndRejectsWhitespace()
    {
        var source = ReadRepositoryFile(
            "GaoApp.Web",
            "Configuration",
            "StartupValidationService.cs");

        Assert.Contains(
            "var rootDomain = options.RootDomain;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "rootDomain.Any(char.IsWhiteSpace)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "var adminSubdomain = options.AdminSubdomain;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "adminSubdomain.Any(char.IsWhiteSpace)",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "options.RootDomain?.Trim()",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "options.AdminSubdomain?.Trim()",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionStorageValidation_RequiresFullyQualifiedPath()
    {
        var source = ReadRepositoryFile(
            "GaoApp.Web",
            "Configuration",
            "StartupValidationService.cs");

        Assert.Contains(
            "!Path.IsPathFullyQualified(options.UploadRoot)",
            source,
            StringComparison.Ordinal);
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;

        while ((index = source.IndexOf(
                   value,
                   index,
                   StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string ReadRepositoryFile(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null &&
               !File.Exists(Path.Combine(current.FullName, "GaoApp.sln")))
        {
            current = current.Parent;
        }

        Assert.NotNull(current);
        return File.ReadAllText(
            Path.Combine(new[] { current!.FullName }.Concat(segments).ToArray()));
    }
}
