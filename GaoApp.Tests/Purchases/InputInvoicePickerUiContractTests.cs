namespace GaoApp.Tests.Purchases;

// R2.4-C2 coverage: owner-invalid candidates remain previewable with accessible block reasons.
public sealed class InputInvoicePickerUiContractTests
{
    [Fact]
    public void Draft_keeps_physical_receiving_and_hides_manager_xml_mapping_work()
    {
        var root = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "Areas", "Admin",
            "Views", "StockDocumentManagement", "Edit.cshtml"));
        var lines = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "Areas", "Admin",
            "Views", "StockDocumentManagement", "_StockDocumentLinesTable.cshtml"));

        Assert.Contains("CanEditReceiptPhysical", view, StringComparison.Ordinal);
        Assert.Contains("CanManageInputInvoiceMapping", view, StringComparison.Ordinal);
        Assert.Contains("canEditPhysical", lines, StringComparison.Ordinal);
        Assert.Contains("canManageInputInvoiceMapping", lines, StringComparison.Ordinal);
        Assert.Contains("js-inline-line-qty", lines, StringComparison.Ordinal);
        Assert.Contains("btn-map-input-invoice-line", lines, StringComparison.Ordinal);
        Assert.Contains("@if (canManageInputInvoiceMapping)", lines,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Pending_approval_workbench_moves_xml_mapping_into_bill_pricing()
    {
        var root = FindRepositoryRoot();
        var workbench = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "Areas", "Admin",
            "Views", "StockDocumentManagement", "_CommercialApprovalWorkbench.cshtml"));
        Assert.Contains("pricingXmlImport", workbench, StringComparison.Ordinal);
        Assert.Contains("pricingXmlPanel", workbench, StringComparison.Ordinal);
        Assert.Contains("pricingXmlAddAll", workbench, StringComparison.Ordinal);
        Assert.Contains("Nhập nhanh từ hóa đơn XML", workbench, StringComparison.Ordinal);
        Assert.Contains("bill nhập tay và chương trình đang có được giữ", workbench, StringComparison.Ordinal);
        Assert.DoesNotContain("data-workbench-tab=\"xml\"", workbench, StringComparison.Ordinal);
        Assert.DoesNotContain("btn-map-input-invoice-line", workbench, StringComparison.Ordinal);
    }

    [Fact]
    public void Confirmed_receipt_exposes_waiting_link_unlink_and_coherent_relink_ux()
    {
        var root = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "Areas", "Admin",
            "Views", "StockDocumentManagement", "Edit.cshtml"));
        var modal = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "Areas", "Admin",
            "Views", "StockDocumentManagement", "_InputInvoicePickerModal.cshtml"));
        var picker = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "wwwroot", "Admin",
            "js", "input-invoice-picker.js"));
        var receipt = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "wwwroot", "Admin",
            "js", "stock-document-management.js"));
        var controller = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "Areas", "Admin",
            "Controllers", "StockDocumentsController.cs"));
        var combined = view + modal + picker + receipt + controller;

        Assert.Contains("Chờ hóa đơn XML", combined, StringComparison.Ordinal);
        Assert.Contains("input-invoices/association", combined, StringComparison.Ordinal);
        Assert.Contains("input-invoices/relink", combined, StringComparison.Ordinal);
        Assert.Contains("inputInvoiceAssociationReason", combined, StringComparison.Ordinal);
        Assert.Contains("expectedCurrentInputInvoiceHeadId", combined, StringComparison.Ordinal);
        Assert.Contains("maxlength=\"1000\"", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aria-live", combined, StringComparison.Ordinal);
        Assert.Contains("focus", picker + receipt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Linked_invoice_card_should_render_a_two_digit_date_without_time()
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(
            root,
            "GaoApp.Web",
            "wwwroot",
            "Admin",
            "js",
            "stock-document-management.js"));
        var linkedCardStart = script.IndexOf(
            "function renderInputInvoiceList",
            StringComparison.Ordinal);
        Assert.True(linkedCardStart >= 0);
        var linkedCardEnd = script.IndexOf(
            "function bindLinkedInputInvoiceActions",
            linkedCardStart,
            StringComparison.Ordinal);

        Assert.True(linkedCardEnd > linkedCardStart);
        var linkedCard = script[linkedCardStart..linkedCardEnd];

        Assert.Contains(
            "formatLinkedInputInvoiceDate(invoice.invoiceDate)",
            linkedCard,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "formatDate(invoice.invoiceDate)",
            linkedCard,
            StringComparison.Ordinal);

        var formatterStart = script.IndexOf(
            "function formatLinkedInputInvoiceDate",
            StringComparison.Ordinal);
        Assert.True(formatterStart >= 0);
        var formatterEnd = script.IndexOf(
            "function formatNumber",
            formatterStart,
            StringComparison.Ordinal);
        Assert.True(formatterEnd > formatterStart);
        var formatter = script[formatterStart..formatterEnd];

        Assert.Contains("day: '2-digit'", formatter, StringComparison.Ordinal);
        Assert.Contains("month: '2-digit'", formatter, StringComparison.Ordinal);
        Assert.Contains("year: 'numeric'", formatter, StringComparison.Ordinal);
        Assert.Contains(
            "toLocaleDateString('vi-VN'",
            formatter,
            StringComparison.Ordinal);
        Assert.DoesNotContain("toLocaleString", formatter, StringComparison.Ordinal);
    }

    [Fact]
    public void Receipt_should_expose_bounded_picker_and_no_superseded_normal_flow()
    {
        var root = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "Areas", "Admin", "Views", "StockDocumentManagement", "Edit.cshtml"));
        var modal = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "Areas", "Admin", "Views", "StockDocumentManagement", "_InputInvoicePickerModal.cshtml"));
        var script = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "wwwroot", "Admin", "js", "input-invoice-picker.js"));
        var controller = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "Areas", "Admin", "Controllers", "StockDocumentsController.cs"));
        var combined = view + modal + script + controller;

        Assert.Contains("Chọn hóa đơn", combined, StringComparison.Ordinal);
        Assert.Contains("input-invoices/picker/candidates", combined, StringComparison.Ordinal);
        Assert.Contains("input-invoices/picker/select", combined, StringComparison.Ordinal);
        Assert.Contains("aria-live", modal, StringComparison.Ordinal);
        Assert.Contains("@media", modal, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: minmax(19rem, 35%) minmax(0, 65%)", modal, StringComparison.Ordinal);
        Assert.Contains("min-height: 2.5rem", modal, StringComparison.Ordinal);
        Assert.Contains("overflow-x: hidden", modal, StringComparison.Ordinal);
        Assert.Contains("<span>Ngày</span>", modal, StringComparison.Ordinal);
        Assert.Contains("<span>Ký hiệu</span>", modal, StringComparison.Ordinal);
        Assert.Contains("<span>Số hóa đơn</span>", modal, StringComparison.Ordinal);
        Assert.Contains("<span>Trạng thái</span>", modal, StringComparison.Ordinal);
        Assert.Contains("candidates.findIndex", script, StringComparison.Ordinal);
        Assert.Contains("candidate.linkedCurrentReceipt", script, StringComparison.Ordinal);
        Assert.Contains("? 'Đã chọn'", script, StringComparison.Ordinal);
        Assert.Contains("await choose(linkedIndex)", script, StringComparison.Ordinal);
        Assert.Contains("select.disabled = !selected?.selectionAllowed", script, StringComparison.Ordinal);
        Assert.Contains("modalInstance.hide()", script, StringComparison.Ordinal);
        Assert.DoesNotContain("formatMoney(candidate.totalPaymentAmount)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("candidate.sellerName", script, StringComparison.Ordinal);
        Assert.DoesNotContain("candidate.sellerTaxCode", script, StringComparison.Ordinal);
        Assert.Contains("Xem hóa đơn", view + File.ReadAllText(Path.Combine(root, "GaoApp.Web", "wwwroot", "Admin", "js", "stock-document-management.js")), StringComparison.Ordinal);
        Assert.Contains("Gỡ liên kết hóa đơn?", view, StringComparison.Ordinal);
        Assert.Contains("PDF, XML và hóa đơn gốc không bị xóa.", view, StringComparison.Ordinal);
        Assert.Contains("js-view-linked-input-invoice", combined + File.ReadAllText(Path.Combine(root, "GaoApp.Web", "wwwroot", "Admin", "js", "stock-document-management.js")), StringComparison.Ordinal);
        Assert.Contains("js-unlink-input-invoice", combined + File.ReadAllText(Path.Combine(root, "GaoApp.Web", "wwwroot", "Admin", "js", "stock-document-management.js")), StringComparison.Ordinal);
        Assert.Contains("input-invoices/{inputInvoiceId:int}/unlink", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("MST bán:", File.ReadAllText(Path.Combine(root, "GaoApp.Web", "wwwroot", "Admin", "js", "stock-document-management.js")), StringComparison.Ordinal);
        Assert.DoesNotContain("Tổng thanh toán:", File.ReadAllText(Path.Combine(root, "GaoApp.Web", "wwwroot", "Admin", "js", "stock-document-management.js")), StringComparison.Ordinal);
        Assert.DoesNotContain("input-invoices/upload-xml", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("Upload XML", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("select-candidate", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("align-receipt", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("correct-canonical", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void Linked_invoice_card_should_render_persistent_owner_drift_warning_without_auto_unlink()
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(
            root, "GaoApp.Web", "wwwroot", "Admin", "js",
            "stock-document-management.js"));

        Assert.Contains("invoice.ownerWarningReasonCode", script, StringComparison.Ordinal);
        Assert.Contains("invoice.ownerWarningMessage", script, StringComparison.Ordinal);
        Assert.Contains("alert-warning", script, StringComparison.Ordinal);
        Assert.DoesNotContain("autoUnlink", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Receipt_picker_prerequisites_should_be_rendered_from_supplier_and_tax_code()
    {
        var root = FindRepositoryRoot();
        var dto = File.ReadAllText(Path.Combine(
            root,
            "GaoApp.Application",
            "DTOs",
            "Inventory",
            "StockDocumentDto.cs"));
        var service = File.ReadAllText(Path.Combine(
            root,
            "GaoApp.Application",
            "Services",
            "Inventory",
            "StockDocumentService.cs"));
        var view = File.ReadAllText(Path.Combine(
            root,
            "GaoApp.Web",
            "Areas",
            "Admin",
            "Views",
            "StockDocumentManagement",
            "Edit.cshtml"));

        Assert.Contains("public string? SupplierTaxCode { get; set; }", dto, StringComparison.Ordinal);
        Assert.Contains("SupplierTaxCode = document.Supplier?.TaxCode", service, StringComparison.Ordinal);
        Assert.Contains("Chưa chọn nhà cung cấp.", view, StringComparison.Ordinal);
        Assert.Contains("Vui lòng chọn nhà cung cấp trước khi chọn hóa đơn.", view, StringComparison.Ordinal);
        Assert.Contains("Nhà cung cấp chưa có mã số thuế.", view, StringComparison.Ordinal);
        Assert.Contains("Vui lòng cập nhật danh mục nhà cung cấp trước khi chọn hóa đơn.", view, StringComparison.Ordinal);
        Assert.Contains("canOpenInputInvoicePicker", view, StringComparison.Ordinal);
        Assert.Contains("disabled=\"@(!canOpenInputInvoicePicker)\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Được xác định từ phiếu", view, StringComparison.Ordinal);
    }

    [Fact]
    public void Manual_association_uses_fixed_receipt_line_and_optional_remember()
    {
        var root = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "Areas", "Admin",
            "Views", "StockDocumentManagement", "Edit.cshtml"));
        var lines = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "Areas", "Admin",
            "Views", "StockDocumentManagement", "_StockDocumentLinesTable.cshtml"));
        var script = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "wwwroot", "Admin",
            "js", "stock-document-management.js"));

        Assert.Contains("mapInputInvoiceLineModal", view, StringComparison.Ordinal);
        Assert.Contains("mapRememberItemCatalogMapping", view, StringComparison.Ordinal);
        Assert.Contains("Ghi nhớ lựa chọn này cho lần sau", view, StringComparison.Ordinal);
        Assert.Contains("Xác nhận ghép", view, StringComparison.Ordinal);
        Assert.DoesNotContain("mapCatalogProductVariant", view, StringComparison.Ordinal);
        Assert.DoesNotContain("mapCatalogUnitConversion", view, StringComparison.Ordinal);
        Assert.Contains("js-item-map-status", lines, StringComparison.Ordinal);
        Assert.Contains("rememberItemCatalogMapping", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Hãy chọn ProductVariant và ProductUnitConversion", script,
            StringComparison.Ordinal);
        Assert.DoesNotContain("autoUnlink", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mapping-management", view + script,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Manual_association_reenables_remember_when_modal_defaults_to_xml()
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "wwwroot", "Admin",
            "js", "stock-document-management.js"));

        var openerStart = script.IndexOf("function bindOpenMapInputInvoiceLineModal()",
            StringComparison.Ordinal);
        var openerEnd = script.IndexOf("async function ensureInputInvoicesLoaded()",
            StringComparison.Ordinal);
        Assert.True(openerStart >= 0 && openerEnd > openerStart);
        var opener = script[openerStart..openerEnd];

        Assert.Contains("document.getElementById('mapUseInputInvoice').checked = true;",
            opener, StringComparison.Ordinal);
        Assert.Contains("document.getElementById('mapRememberItemCatalogMapping').checked = false;",
            opener, StringComparison.Ordinal);
        Assert.Contains(
            "document.getElementById('mapUseInputInvoice').dispatchEvent(new Event('change'));",
            opener,
            StringComparison.Ordinal);

        var bindingStart = script.IndexOf("function bindSaveInputInvoiceLineMap()",
            StringComparison.Ordinal);
        var bindingEnd = script.IndexOf("function bindToggleAllInputInvoiceLines()",
            StringComparison.Ordinal);
        Assert.True(bindingStart >= 0 && bindingEnd > bindingStart);
        var binding = script[bindingStart..bindingEnd];

        Assert.Contains("rememberToggle.disabled = !useToggle.checked;",
            binding, StringComparison.Ordinal);
        Assert.Contains("if (!useToggle.checked) rememberToggle.checked = false;",
            binding, StringComparison.Ordinal);
    }

    [Fact]
    public void Item_catalog_badges_distinguish_confirmed_cross_unit_and_unresolved_states()
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "wwwroot", "Admin",
            "js", "stock-document-management.js"));
        var presentationStart = script.IndexOf("function getItemCatalogStatePresentation(mapping)",
            StringComparison.Ordinal);
        var presentationEnd = script.IndexOf("function renderItemCatalogStatusBadge(mapping)",
            StringComparison.Ordinal);
        Assert.True(presentationStart >= 0 && presentationEnd > presentationStart);
        var presentation = script[presentationStart..presentationEnd];

        Assert.Contains("state === 'Confirmed'", presentation, StringComparison.Ordinal);
        Assert.Contains("label: 'Đã nhớ'", presentation, StringComparison.Ordinal);
        Assert.Contains("mapping?.reasonCode === 'CrossUnitProductInherited'", presentation,
            StringComparison.Ordinal);
        Assert.Contains("label: 'Đã nhận diện'", presentation, StringComparison.Ordinal);
        Assert.Contains("mapping?.reasonCode === 'NameUnitSuggestionRequiresConfirmation'",
            presentation, StringComparison.Ordinal);
        Assert.Contains("label: 'Gợi ý cần xác nhận'", presentation, StringComparison.Ordinal);
        Assert.Contains("label: 'Chưa nhận diện'", presentation, StringComparison.Ordinal);
    }

    [Fact]
    public void Link_relink_unlink_and_manual_association_share_the_final_preview_refresh()
    {
        var root = FindRepositoryRoot();
        var picker = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "wwwroot", "Admin",
            "js", "input-invoice-picker.js"));
        var receipt = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "wwwroot", "Admin",
            "js", "stock-document-management.js"));

        Assert.Contains("${isRelink ? 'relink' : 'picker/select'}", picker,
            StringComparison.Ordinal);
        Assert.Contains("await window.loadInputInvoicesForStockDocument();", picker,
            StringComparison.Ordinal);
        Assert.Contains("await loadInputInvoicesForStockDocument();", receipt,
            StringComparison.Ordinal);
        Assert.Contains("await refreshCommercialReconciliationPreviewAfterXmlMutation();",
            receipt, StringComparison.Ordinal);

        var saveStart = receipt.IndexOf("function bindSaveInputInvoiceLineMap()",
            StringComparison.Ordinal);
        var saveEnd = receipt.IndexOf("async function refreshInputInvoiceMappingWorkspace()",
            saveStart, StringComparison.Ordinal);
        Assert.True(saveStart >= 0 && saveEnd > saveStart);
        var manualAssociation = receipt[saveStart..saveEnd];
        Assert.Contains("await refreshInputInvoiceMappingWorkspace();", manualAssociation,
            StringComparison.Ordinal);
        Assert.DoesNotContain("await loadInputInvoiceReconciliation();", manualAssociation,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
