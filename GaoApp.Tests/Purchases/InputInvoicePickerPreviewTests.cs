namespace GaoApp.Tests.Purchases;

public sealed class InputInvoicePickerPreviewTests
{
    [Fact]
    public void Preview_contract_should_stream_pdf_and_render_encoded_xml_partial()
    {
        var root = FindRepositoryRoot();
        var controller = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "Areas", "Admin", "Controllers", "StockDocumentsController.cs"));
        var partial = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "Areas", "Admin", "Views", "StockDocumentManagement", "_InputInvoiceXmlPreview.cshtml"));
        var script = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "wwwroot", "Admin", "js", "input-invoice-picker.js"));
        var linkedScript = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "wwwroot", "Admin", "js", "stock-document-management.js"));

        Assert.Contains("input-invoices/picker/pdf", controller, StringComparison.Ordinal);
        Assert.Contains("application/pdf", controller, StringComparison.Ordinal);
        Assert.Contains("input-invoices/picker/xml", controller, StringComparison.Ordinal);
        Assert.Contains("Dựng từ XML", partial, StringComparison.Ordinal);
        Assert.DoesNotContain("Html.Raw", partial, StringComparison.Ordinal);
        Assert.Contains("Không mở được PDF", script, StringComparison.Ordinal);
        Assert.Contains("input-invoices/{inputInvoiceId:int}/preview/pdf", controller, StringComparison.Ordinal);
        Assert.Contains("input-invoices/{inputInvoiceId:int}/preview/xml", controller, StringComparison.Ordinal);
        Assert.Contains("GetLinkedPdfAsync", controller, StringComparison.Ordinal);
        Assert.Contains("GetLinkedXmlPreviewAsync", controller, StringComparison.Ordinal);
        Assert.Contains("`${baseUrl}/pdf`", linkedScript, StringComparison.Ordinal);
        Assert.Contains("`${baseUrl}/xml`", linkedScript, StringComparison.Ordinal);
        Assert.True(linkedScript.IndexOf("`${baseUrl}/pdf`", StringComparison.Ordinal) <
                    linkedScript.IndexOf("`${baseUrl}/xml`", StringComparison.Ordinal));
        Assert.Contains("split/invoice-picker/browse", controller, StringComparison.Ordinal);
        Assert.Contains("split/invoice-picker/pdf", controller, StringComparison.Ordinal);
        Assert.Contains("split/invoice-picker/xml", controller, StringComparison.Ordinal);
        Assert.Contains("BrowseForSupplierAsync", controller, StringComparison.Ordinal);
        Assert.Contains("GetPdfForSupplierAsync", controller, StringComparison.Ordinal);
        Assert.Contains("GetXmlPreviewForSupplierAsync", controller, StringComparison.Ordinal);
        Assert.Contains("Mã hàng XML", partial, StringComparison.Ordinal);
        Assert.Contains("SupplierItemCode", partial, StringComparison.Ordinal);
        Assert.Contains("supplierItemCode", linkedScript, StringComparison.Ordinal);
        Assert.Contains("Nhận diện", linkedScript, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
