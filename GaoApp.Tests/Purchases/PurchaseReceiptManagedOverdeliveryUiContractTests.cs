namespace GaoApp.Tests.Purchases;

public sealed class PurchaseReceiptManagedOverdeliveryUiContractTests
{
    [Fact]
    public void Manager_confirmation_requires_explicit_acceptance_but_keeps_reason_optional()
    {
        var root = FindRepositoryRoot();
        var edit = File.ReadAllText(Path.Combine(
            root, "GaoApp.Web", "Areas", "Admin", "Views",
            "StockDocumentManagement", "Edit.cshtml"));
        var workbench = File.ReadAllText(Path.Combine(
            root, "GaoApp.Web", "Areas", "Admin", "Views",
            "StockDocumentManagement", "_CommercialApprovalWorkbench.cshtml"));
        var script = File.ReadAllText(Path.Combine(
            root, "GaoApp.Web", "wwwroot", "Admin", "js",
            "purchase-receipt-approval.js"));

        Assert.Contains("id=\"acceptOverdelivery\"", edit);
        Assert.Contains("id=\"overdeliveryNote\"", edit);
        Assert.DoesNotContain("required id=\"overdeliveryNote\"", edit);
        Assert.Contains("ProjectedOverdeliveryQuantity", workbench);
        Assert.Contains("acceptOverdelivery:", script);
        Assert.Contains("overdeliveryNote:", script);
        Assert.Contains("requireOverdeliveryAcceptance", script);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
