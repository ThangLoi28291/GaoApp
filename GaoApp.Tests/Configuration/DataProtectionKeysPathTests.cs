using GaoApp.Infrastructure.Security;
using System.Security.Cryptography;
using System.Text;

namespace GaoApp.Tests.Configuration;

public sealed class DataProtectionKeysPathTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_MissingOrBlankPath_FailsWithConfigurationKey(
        string? configuredPath)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            DataProtectionKeysPath.Resolve(
                configuredPath,
                Path.GetTempPath(),
                AppContext.BaseDirectory,
                isProduction: false));

        Assert.Contains(
            DataProtectionKeysPath.ConfigurationKey,
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_RelativeDevelopmentPath_UsesContentRoot()
    {
        var contentRoot = CreateUniquePath();

        var result = DataProtectionKeysPath.Resolve(
            Path.Combine("keys", "ring"),
            contentRoot,
            AppContext.BaseDirectory,
            isProduction: false);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(contentRoot, "keys", "ring")),
            result);
    }

    [Fact]
    public void Resolve_RelativeProductionPath_FailsWithConfigurationKey()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            DataProtectionKeysPath.Resolve(
                "keys",
                CreateUniquePath(),
                AppContext.BaseDirectory,
                isProduction: true));

        Assert.Contains(
            DataProtectionKeysPath.ConfigurationKey,
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_WindowsDriveRelativeProductionPath_Fails()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DataProtectionKeysPath.Resolve(
                @"C:keys",
                CreateUniquePath(),
                AppContext.BaseDirectory,
                isProduction: true));

        Assert.Contains(
            DataProtectionKeysPath.ConfigurationKey,
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_WindowsRootRelativeProductionPath_Fails()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DataProtectionKeysPath.Resolve(
                @"\keys",
                CreateUniquePath(),
                AppContext.BaseDirectory,
                isProduction: true));

        Assert.Contains(
            DataProtectionKeysPath.ConfigurationKey,
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_WindowsUncProductionPathOutsideApplication_Passes()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const string uncPath = @"\\r13-review.invalid\keys\gaoapp";

        var result = DataProtectionKeysPath.Resolve(
            uncPath,
            CreateUniquePath(),
            AppContext.BaseDirectory,
            isProduction: true);

        Assert.Equal(Path.GetFullPath(uncPath), result);
    }

    [Fact]
    public void Resolve_AbsoluteProductionPathOutsideApplication_Passes()
    {
        var contentRoot = CreateUniquePath();
        var applicationBase = CreateUniquePath();
        var externalPath = CreateUniquePath();

        var result = DataProtectionKeysPath.Resolve(
            externalPath,
            contentRoot,
            applicationBase,
            isProduction: true);

        Assert.Equal(Path.GetFullPath(externalPath), result);
    }

    [Fact]
    public void Resolve_ProductionPathInsideContentRoot_Fails()
    {
        var contentRoot = CreateUniquePath();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DataProtectionKeysPath.Resolve(
                Path.Combine(contentRoot, "keys"),
                contentRoot,
                CreateUniquePath(),
                isProduction: true));

        Assert.Contains(
            DataProtectionKeysPath.ConfigurationKey,
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_ProductionPathInsideApplicationBase_Fails()
    {
        var applicationBase = CreateUniquePath();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DataProtectionKeysPath.Resolve(
                Path.Combine(applicationBase, "keys"),
                CreateUniquePath(),
                applicationBase,
                isProduction: true));

        Assert.Contains(
            DataProtectionKeysPath.ConfigurationKey,
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DirectoryValidator_PathPointingToFile_FailsWithoutChangingFile()
    {
        var directory = Directory.CreateDirectory(CreateUniquePath()).FullName;
        var file = Path.Combine(directory, "existing-key-sentinel.xml");
        var original = Encoding.UTF8.GetBytes("benign-sentinel");
        File.WriteAllBytes(file, original);

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                new DataProtectionKeysDirectoryValidator().Validate(file));

            Assert.Contains(
                DataProtectionKeysPath.ConfigurationKey,
                exception.Message,
                StringComparison.Ordinal);
            Assert.Equal(original, File.ReadAllBytes(file));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DirectoryValidator_PreservesExistingFilesAndRemovesOnlyProbe()
    {
        var directory = Directory.CreateDirectory(CreateUniquePath()).FullName;
        var sentinel = Path.Combine(directory, "existing-key-sentinel.xml");
        File.WriteAllText(sentinel, "benign-sentinel");
        var beforeHash = SHA256.HashData(File.ReadAllBytes(sentinel));

        try
        {
            new DataProtectionKeysDirectoryValidator().Validate(directory);

            Assert.True(File.Exists(sentinel));
            Assert.Equal(beforeHash, SHA256.HashData(File.ReadAllBytes(sentinel)));
            Assert.Equal(new[] { "existing-key-sentinel.xml" },
                Directory.GetFiles(directory).Select(Path.GetFileName).ToArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ResolverAndRuntimeState_UseSameCanonicalPath()
    {
        var canonicalPath = DataProtectionKeysPath.Resolve(
            Path.Combine("keys", "..", "canonical-keys"),
            CreateUniquePath(),
            AppContext.BaseDirectory,
            isProduction: false);
        var state = new DataProtectionKeysPathState();

        state.Initialize(canonicalPath);
        state.Initialize(Path.GetFullPath(canonicalPath));

        Assert.Equal(canonicalPath, state.GetRequiredPath());
    }

    private static string CreateUniquePath()
        => Path.Combine(
            Path.GetTempPath(),
            "GaoApp-R1.3-Tests",
            Guid.NewGuid().ToString("N"));
}
