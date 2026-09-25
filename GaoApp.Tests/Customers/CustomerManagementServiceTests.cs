namespace GaoApp.Tests.Customers;

public sealed class CustomerManagementServiceTests
{
    private static readonly string ServicePath = Path.Combine(
        FindRepositoryRoot(), "GaoApp.Application", "Services", "Customers", "CustomerManagementService.cs");

    [Fact]
    public void Service_should_normalize_bounded_fields_and_use_current_store()
    {
        var source = File.ReadAllText(ServicePath);

        Assert.Contains("_currentStore.StoreId", source, StringComparison.Ordinal);
        Assert.Contains("CustomerPriceTiers.Retail", source, StringComparison.Ordinal);
        Assert.Contains("CustomerPriceTiers.Wholesale", source, StringComparison.Ordinal);
        Assert.Contains("DuplicateCode", source, StringComparison.Ordinal);
        Assert.Contains("DuplicatePhone", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_should_preserve_legacy_import_reward_fields_and_avoid_delete()
    {
        var source = File.ReadAllText(ServicePath);

        Assert.DoesNotContain("OldCustomerId =", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IsImportedFromOldSystem =", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ImportedRewardAmount =", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CustomerGroup =", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteAsync", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("GAOAPP_REPOSITORY_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot) &&
            File.Exists(Path.Combine(configuredRoot, "GaoApp.sln")))
        {
            return configuredRoot;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GaoApp.sln"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate GaoApp repository root.");
    }
}
