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
            "GaoApp.Migrator",
            "MigrationRunner.cs");

        var validationIndex = source.IndexOf(
            "ValidateSeedOptions();",
            StringComparison.Ordinal);
        var migrationIndex = source.IndexOf(
            "MigrateAsync(ct)",
            StringComparison.Ordinal);

        Assert.True(validationIndex >= 0, "Seed option validation was not found.");
        Assert.True(migrationIndex >= 0, "Database migration call was not found.");
        Assert.True(
            validationIndex < migrationIndex,
            "Seed options must be validated before database migration.");
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
