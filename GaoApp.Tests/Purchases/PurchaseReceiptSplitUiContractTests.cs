using FluentAssertions;

namespace GaoApp.Tests.Purchases;

// R2.4-C2 coverage: split candidates remain previewable when assignment is owner-blocked.
public sealed class PurchaseReceiptSplitUiContractTests
{
    [Fact]
    public void Exact_five_split_routes_and_no_extra_split_route_are_declared()
    {
        var pageController = Read("GaoApp.Web/Areas/Admin/Controllers/StockDocumentManagementController.cs");
        var apiController = Read("GaoApp.Web/Areas/Admin/Controllers/StockDocumentsController.cs");

        pageController.Should().Contain("[HttpGet(\"{id:int}/split\")]");
        apiController.Should().Contain("[HttpPost(\"{id:int}/split\")]");
        apiController.Should().Contain("[HttpGet(\"split/invoice-picker/browse\")]");
        apiController.Should().Contain("[HttpGet(\"split/invoice-picker/pdf\")]");
        apiController.Should().Contain("[HttpGet(\"split/invoice-picker/xml\")]");
        (pageController + apiController).Split("split/invoice-picker/", StringSplitOptions.None)
            .Length.Should().Be(4);
    }

    [Fact]
    public void Edit_exposes_split_only_for_authorized_direct_pending_receipt()
    {
        var view = Read("GaoApp.Web/Areas/Admin/Views/StockDocumentManagement/Edit.cshtml");

        view.Should().Contain("canSplitReceipt");
        view.Should().Contain("PurchaseReceiptSource.Direct");
        view.Should().Contain("StockDocumentStatus.PendingApproval");
        view.Should().Contain("Tách phiếu nhập");
    }

    [Fact]
    public void Workspace_contains_allocation_payment_invoice_date_and_final_confirmation_contracts()
    {
        var view = Read("GaoApp.Web/Areas/Admin/Views/StockDocumentManagement/Split.cshtml");
        var script = Read("GaoApp.Web/wwwroot/Admin/js/split-receipt.js");

        view.Should().Contain("Còn lại");
        view.Should().Contain("Nhà cung cấp");
        view.Should().Contain("Kho");
        view.Should().Contain("Ngày nhập kho dự kiến");
        view.Should().Contain("Xác nhận trạng thái thanh toán");
        script.Should().Contain("addTarget");
        script.Should().Contain("removeTarget");
        script.Should().Contain("invoiceDocumentKey");
        script.Should().Contain("window.confirm");
        script.Should().Contain("resultLinks");
        script.Should().NotContain("/unlink");
    }

    private static string Read(string relativePath)
        => File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
