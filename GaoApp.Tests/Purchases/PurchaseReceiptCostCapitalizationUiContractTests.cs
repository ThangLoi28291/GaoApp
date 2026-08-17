using FluentAssertions;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Domain.Entities;

namespace GaoApp.Tests.Purchases;

public sealed class PurchaseReceiptCostCapitalizationUiContractTests
{
    [Fact]
    public void New_receipts_and_commands_default_both_cost_choices_off()
    {
        var document = new StockDocument();
        var command = new ApprovePurchaseReceiptCommercialRequest();

        document.IncludeVatInInventoryCost.Should().BeFalse();
        document.CapitalizeFreightInInventoryCost.Should().BeFalse();
        command.IncludeVatInInventoryCost.Should().BeFalse();
        command.CapitalizeFreightInInventoryCost.Should().BeFalse();
    }

    [Fact]
    public void Workbench_exposes_explicit_choices_and_sends_them_authoritatively()
    {
        var view = Read("GaoApp.Web/Areas/Admin/Views/StockDocumentManagement/_CommercialApprovalWorkbench.cshtml");
        var script = Read("GaoApp.Web/wwwroot/Admin/js/purchase-receipt-approval.js");

        view.Should().Contain("id=\"includeVatInInventoryCost\"");
        view.Should().Contain("id=\"capitalizeFreightInInventoryCost\"");
        view.Should().Contain("Mặc định ghi nhận riêng");
        script.Should().Contain("includeVatInInventoryCost: includeVatInInventoryCost");
        script.Should().Contain("capitalizeFreightInInventoryCost: capitalizeFreightInInventoryCost");
        script.Should().Contain("amount: capitalizeFreightInInventoryCost");
        script.Should().Contain("Phí được ghi nhận riêng, không vào giá vốn");
        script.Should().NotContain("additionalCost");
    }

    [Fact]
    public void Posting_policy_keeps_commercial_payables_separate_from_inventory_cost_choices()
    {
        var service = Read("GaoApp.Application/Services/Inventory/StockDocumentService.cs");

        service.Should().Contain("document.IncludeVatInInventoryCost");
        service.Should().Contain("document.CapitalizeFreightInInventoryCost");
        service.Should().Contain("CreatePayablesIfNeededAsync");
        service.Should().Contain("line.LineTotal - line.VatAmount");
    }

    private static string Read(string relativePath)
        => File.ReadAllText(Path.Combine(FindRepositoryRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
