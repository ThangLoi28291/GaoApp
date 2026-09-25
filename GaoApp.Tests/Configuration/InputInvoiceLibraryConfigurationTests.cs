using GaoApp.Application.Common.Options;

namespace GaoApp.Tests.Configuration;

public sealed class InputInvoiceLibraryConfigurationTests
{
    [Fact]
    public void Safety_defaults_should_disable_library_and_bound_browse()
    {
        var options = new InputInvoiceLibraryOptions();

        Assert.False(options.Enabled);
        Assert.True(string.IsNullOrEmpty(options.RootPath));
        Assert.InRange(options.MaxMonthPartitions, 1, 12);
        Assert.InRange(options.MaxCandidates, 1, 200);
    }

    [Fact]
    public void Committed_configuration_should_not_hard_code_an_invoice_root()
    {
        var root = FindRepositoryRoot();
        var json = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "appsettings.json"));

        Assert.Contains("\"InputInvoiceLibrary\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Enabled\": false", json, StringComparison.Ordinal);
        Assert.Contains("\"RootPath\": \"\"", json, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
