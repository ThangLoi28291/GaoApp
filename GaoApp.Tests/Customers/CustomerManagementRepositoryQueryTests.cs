namespace GaoApp.Tests.Customers;

public sealed class CustomerManagementRepositoryQueryTests
{
    private static readonly string RepositoryPath = Path.Combine(
        FindRepositoryRoot(), "GaoApp.Infrastructure", "Repositories", "Customers", "CustomerManagementRepository.cs");

    [Fact]
    public void Repository_should_explicitly_scope_every_customer_query_to_store()
    {
        var source = File.ReadAllText(RepositoryPath);

        Assert.Contains("item.StoreId == storeId", source, StringComparison.Ordinal);
        Assert.Contains("AsNoTracking", source, StringComparison.Ordinal);
        Assert.Contains("GetPageAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetSummaryAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetByIdAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_should_cover_approved_search_and_filter_fields()
    {
        var source = File.ReadAllText(RepositoryPath);

        Assert.Contains("AccentInsensitiveSearchCollation", source, StringComparison.Ordinal);
        Assert.Contains("item.Name", source, StringComparison.Ordinal);
        Assert.Contains("item.Code", source, StringComparison.Ordinal);
        Assert.Contains("item.Phone", source, StringComparison.Ordinal);
        Assert.Contains("item.Email", source, StringComparison.Ordinal);
        Assert.Contains("item.TaxCode", source, StringComparison.Ordinal);
        Assert.Contains("PriceTier", source, StringComparison.Ordinal);
        Assert.Contains("HaveDebt", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_should_not_offer_delete_or_touch_reward_order_data()
    {
        var source = File.ReadAllText(RepositoryPath);

        Assert.DoesNotContain("Remove(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CustomerReward", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Orders", source, StringComparison.Ordinal);
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
