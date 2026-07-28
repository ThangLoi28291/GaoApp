namespace GaoApp.Tests.Configuration;

public sealed class StartupOrderingContractTests
{
    [Fact]
    public void WebStartup_ValidatesBeforeDevelopmentMigration()
    {
        var source = ReadRepositoryFile("GaoApp.Web", "Program.cs");

        var validationIndex = source.IndexOf(
            "await app.ValidateStartupAsync();",
            StringComparison.Ordinal);
        var migrationIndex = source.IndexOf(
            "await app.MigrateAndSeedDatabaseAsync();",
            StringComparison.Ordinal);

        Assert.True(validationIndex >= 0, "Startup validation call was not found.");
        Assert.True(migrationIndex >= 0, "Development migration call was not found.");
        Assert.True(
            validationIndex < migrationIndex,
            "Startup validation must run before Development migration/seed.");
    }

    [Fact]
    public void ProductionWeb_DoesNotOwnDatabaseMigration()
    {
        var source = ReadRepositoryFile("GaoApp.Web", "Program.cs");
        var migrationIndex = source.IndexOf(
            "await app.MigrateAndSeedDatabaseAsync();",
            StringComparison.Ordinal);
        var developmentGuardIndex = source.LastIndexOf(
            "if (app.Environment.IsDevelopment())",
            migrationIndex,
            StringComparison.Ordinal);
        var guardEndIndex = source.IndexOf(
            "\n    }",
            migrationIndex,
            StringComparison.Ordinal);

        Assert.True(migrationIndex >= 0, "Web migration call was not found.");
        Assert.True(
            developmentGuardIndex >= 0 &&
            developmentGuardIndex < migrationIndex &&
            guardEndIndex > migrationIndex,
            "Web migration must remain inside the Development-only guard.");
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
            "_configurationValidator.Validate(",
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
    public void Migrator_InspectsBootstrapBeforeAtomicProvisioningDml()
    {
        var source = ReadRepositoryFile(
            "GaoApp.Infrastructure",
            "Data",
            "Migrations",
            "MigrationExecutionPipeline.cs");

        var migrateIndex = source.IndexOf(
            "_migrationExecutor.MigrateAsync(ct)",
            StringComparison.Ordinal);
        var initialInspectionIndex = source.IndexOf(
            "_productionBootstrapper.InspectAsync(ct)",
            StringComparison.Ordinal);
        var transactionIndex = source.IndexOf(
            "_transactionRunner.ExecuteAsync(",
            StringComparison.Ordinal);
        var lockedInspectionIndex = source.IndexOf(
            "_productionBootstrapper.InspectAsync(",
            initialInspectionIndex + 1,
            StringComparison.Ordinal);
        var mandatoryIndex = source.IndexOf(
            "_mandatorySecuritySeeder.SeedAsync(",
            lockedInspectionIndex,
            StringComparison.Ordinal);
        var applyIndex = source.IndexOf(
            "_productionBootstrapper.ApplyAsync(",
            mandatoryIndex,
            StringComparison.Ordinal);

        Assert.True(
            migrateIndex >= 0
            && migrateIndex < initialInspectionIndex
            && initialInspectionIndex < transactionIndex
            && transactionIndex < lockedInspectionIndex
            && lockedInspectionIndex < mandatoryIndex
            && mandatoryIndex < applyIndex,
            "Required order: migrate, read-only bootstrap inspection, transaction, locked inspection, mandatory seed, bootstrap apply.");
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
