namespace GaoApp.Tests.Purchases;

public sealed class ReceivingManagerApprovalUxContractTests
{
    [Fact]
    public void Manager_projection_preserves_outside_po_allocation_and_decision_state()
    {
        var dto = Read("GaoApp.Application/DTOs/Inventory/StockDocumentLineDto.cs");
        var service = Read("GaoApp.Application/Services/Inventory/StockDocumentService.cs");

        Assert.Contains("ReceiptAllocationKind ReceiptAllocationKind", dto);
        Assert.Contains("OutsidePoDecisionStatus OutsidePoDecisionStatus", dto);
        Assert.Contains("ReceiptAllocationKind = x.ReceiptAllocationKind", service);
        Assert.Contains("OutsidePoDecisionStatus = x.OutsidePoDecisionStatus", service);
    }

    [Fact]
    public void Manager_page_uses_exception_first_identity_summary_and_decision_queue()
    {
        var edit = Read("GaoApp.Web/Areas/Admin/Views/StockDocumentManagement/Edit.cshtml");
        var workbench = Read("GaoApp.Web/Areas/Admin/Views/StockDocumentManagement/_CommercialApprovalWorkbench.cshtml");

        Assert.Contains("Nhận hàng —", edit);
        Assert.Contains("Model.PurchaseOrderTitle", edit);
        Assert.Contains("Model.PurchaseOrderNumber", edit);
        Assert.Contains("Model.SupplierName", edit);
        Assert.Contains("Phiếu nhận @Model.DocumentNo", edit);
        Assert.Contains("sd-manager-summary", edit);
        Assert.Contains("Cần xử lý trước khi duyệt", workbench);
        Assert.Contains("Chấp nhận ngoài PO", workbench);
        Assert.Contains("Loại khỏi phiếu", workbench);
        Assert.Contains("ReceiptAllocationKind.OutsidePo", workbench);
        Assert.Contains("OutsidePoDecisionStatus.Pending", workbench);
        Assert.Contains("id=\"acceptOverdelivery\"", workbench);
    }

    [Fact]
    public void Outside_po_actions_reuse_existing_endpoint_with_rowversion_and_antiforgery()
    {
        var edit = Read("GaoApp.Web/Areas/Admin/Views/StockDocumentManagement/Edit.cshtml");
        var script = Read("GaoApp.Web/wwwroot/Admin/js/stock-document-management.js");
        var provisional = Read("GaoApp.Web/wwwroot/Admin/js/provisional-receiving.js");

        Assert.Contains("managerOutsidePoAntiforgery", edit);
        Assert.Contains("/admin/purchase-receiving/${documentId}/outside/${lineId}", script);
        Assert.Contains("RequestVerificationToken", script);
        Assert.Contains("window.stockDocumentRowVersion", script);
        Assert.Contains("window.stockDocumentRowVersion.current()", script);
        Assert.Contains("window.stockDocumentRowVersion.update(api.data?.rowVersion)", script);
        Assert.Contains("rowVersion: rowVersion", script);
        Assert.Contains("accept: accept", script);
        Assert.Contains("applyState(result);", provisional);
        Assert.Contains("window.stockDocumentRowVersion.update", provisional);
    }

    [Fact]
    public void Goods_view_is_simplified_and_keeps_commercial_inputs_reachable()
    {
        var workbench = Read("GaoApp.Web/Areas/Admin/Views/StockDocumentManagement/_CommercialApprovalWorkbench.cshtml");

        Assert.Contains(">Sản phẩm<", workbench);
        Assert.Contains(">Thực nhận<", workbench);
        Assert.Contains(">Giá nhập<", workbench);
        Assert.Contains(">VAT<", workbench);
        Assert.Contains(">Thành tiền<", workbench);
        Assert.Contains(">Trạng thái<", workbench);
        Assert.DoesNotContain("<th>Giá gần nhất</th>", workbench);
        Assert.DoesNotContain("<th>Tiền VAT</th>", workbench);
        Assert.DoesNotContain("<th>Phí phân bổ</th>", workbench);
        Assert.Contains("commercial-unit-price", workbench);
        Assert.Contains("commercial-tax", workbench);
        Assert.Contains("commercial-last-price", workbench);
    }

    [Fact]
    public void Optional_xml_freight_sticky_actions_and_confirmation_use_progressive_disclosure()
    {
        var edit = Read("GaoApp.Web/Areas/Admin/Views/StockDocumentManagement/Edit.cshtml");
        var workbench = Read("GaoApp.Web/Areas/Admin/Views/StockDocumentManagement/_CommercialApprovalWorkbench.cshtml");
        var css = Read("GaoApp.Web/wwwroot/Admin/css/purchase-receipt-approval.css");

        Assert.Contains("Đối chiếu XML — tùy chọn", edit);
        Assert.Contains("<details", edit);
        Assert.Contains("sd-optional-xml", edit);
        Assert.Contains("<details", workbench);
        Assert.Contains("sd-commercial-freight", workbench);
        Assert.Contains("Phí vận chuyển: Không có", workbench);
        Assert.Contains("sd-manager-blocker-summary", workbench);
        Assert.Contains("Trả về chỉnh sửa", workbench);
        Assert.Contains("Duyệt và ghi sổ", workbench);
        Assert.Contains("Xác nhận duyệt và ghi sổ", edit);
        Assert.Contains("Tồn kho, FIFO/cost và công nợ sẽ được ghi một lần.", edit);
        Assert.Contains("@media (max-width: 767.98px)", css);
        Assert.Contains("sd-manager-goods", workbench);
        Assert.Contains(".sd-commercial-sticky-actions", css);
    }

    [Fact]
    public void Manager_quantities_show_authoritative_product_base_unit_names()
    {
        var dto = Read("GaoApp.Application/DTOs/Inventory/StockDocumentLineDto.cs");
        var service = Read("GaoApp.Application/Services/Inventory/StockDocumentService.cs");
        var workbench = Read("GaoApp.Web/Areas/Admin/Views/StockDocumentManagement/_CommercialApprovalWorkbench.cshtml");

        Assert.Contains("string BaseUnitName", dto);
        Assert.Contains("bool IsBaseUnit", dto);
        Assert.Contains("BaseUnitName = x.ProductVariant?.Product?.BaseUnit?.Name", service);
        Assert.Contains("x.UnitId == x.ProductVariant.Product.BaseUnitId", service);
        Assert.Contains("@line.ProjectedOverdeliveryQuantity.ToString(\"0.###\") @line.BaseUnitName", workbench);
        Assert.Contains("@if (!line.IsBaseUnit)", workbench);
        Assert.Contains("@line.BaseQuantity.ToString(\"0.###\") @line.BaseUnitName", workbench);
        Assert.DoesNotContain("đơn vị gốc", workbench);
    }

    private static string Read(string relativePath)
        => File.ReadAllText(Path.Combine(FindRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
