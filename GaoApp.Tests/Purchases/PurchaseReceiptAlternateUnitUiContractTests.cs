namespace GaoApp.Tests.Purchases;

public sealed class PurchaseReceiptAlternateUnitUiContractTests
{
    [Fact]
    public void Purchase_order_receipt_uses_server_provided_units_and_has_no_free_text_factor()
    {
        var root = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "Areas", "Admin", "Views", "PurchaseOrders", "Details.cshtml"));
        var script = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "wwwroot", "Admin", "js", "purchase-order-details.js"));

        Assert.Contains("line.AllowedReceiptUnits", view);
        Assert.Contains("Lines[@ri].ReceiptUnitId", view);
        Assert.Contains("unit.MaximumReceiptQuantity", view);
        Assert.Contains("line.AvailableToAllocateQuantity", view);
        Assert.Contains("Tương đương", view);
        Assert.Contains("receipt-quantity", script);
        Assert.DoesNotContain("name=\"ConversionFactor\"", view);
        Assert.DoesNotContain("overdelivery", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reopen", view, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
