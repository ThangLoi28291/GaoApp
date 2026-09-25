namespace GaoApp.Tests.Purchases;

public sealed class ReceivingWorkbenchUiContractTests
{
    [Fact]
    public void Workbench_is_rollout_gated_and_disabled_by_default()
    {
        var root = FindRoot();
        var settings = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "appsettings.json"));
        var controller = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "Areas", "Admin", "Controllers", "PurchaseReceivingController.cs"));
        Assert.Contains("\"ReceivingWorkbench\"", settings);
        Assert.Contains("\"Enabled\": false", settings);
        Assert.Contains("ReceivingWorkbench:Enabled", controller);
        Assert.Contains("if (!Enabled) return NotFound();", controller);
    }

    [Fact]
    public void Purchase_order_details_uses_workbench_or_legacy_create_and_keeps_receipt_history()
    {
        var view = File.ReadAllText(Path.Combine(
            FindRoot(), "GaoApp.Web", "Areas", "Admin", "Views", "PurchaseOrders", "Details.cshtml"));

        Assert.Contains("var receivingWorkbenchEnabled =", view);
        Assert.Contains("receivingWorkbenchEnabled && pendingReceipt != null", view);
        Assert.Contains("receivingWorkbenchEnabled && Model.CanCreateReceipt", view);
        Assert.Contains("!receivingWorkbenchEnabled && Model.CanCreateReceipt", view);
        Assert.Contains("asp-controller=\"PurchaseReceiving\"", view);
        Assert.Contains("asp-action=\"Start\"", view);
        Assert.Contains("asp-action=\"CreateReceipt\"", view);
        Assert.Contains("Phiếu nhập liên quan", view);
    }

    [Fact]
    public void Unified_scan_search_autosave_lease_undo_and_accessibility_contract_is_rendered()
    {
        var root = FindRoot();
        var view = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "Areas", "Admin", "Views", "PurchaseReceiving", "Index.cshtml"));
        var script = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "wwwroot", "Admin", "js", "purchase-receiving-workbench.js"));
        var css = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "wwwroot", "Admin", "css", "purchase-receiving-workbench.css"));

        Assert.Contains("Quét barcode hoặc tìm sản phẩm...", view);
        Assert.Contains("id=\"rwReceiveLookup\"", view);
        Assert.Contains("id=\"rwReceiveQuantity\"", view);
        Assert.Contains("id=\"rwResolvedItem\"", view);
        Assert.DoesNotContain("id=\"rwBarcode\"", view);
        Assert.DoesNotContain("id=\"rwProduct\"", view);
        Assert.Contains("Ngoài PO", view);
        Assert.Contains("rw-outside-accept", view);
        Assert.Contains("rw-outside-reject", view);
        Assert.Contains("CanApproveOutsidePo", view);
        Assert.Contains("Hoàn tác", view);
        Assert.Contains("Hoàn tất nhận hàng", view);
        Assert.Contains("id=\"rwOpenCamera\"", view);
        Assert.Contains("id=\"rwAllocationModal\"", view);
        Assert.Contains("aria-live=\"polite\"", view);
        Assert.Contains("aria-label", view);
        Assert.Contains("window.setInterval", script);
        Assert.Contains("30000", script);
        Assert.Contains("crypto.randomUUID", script);
        Assert.Contains("sessionStorage", script);
        Assert.Contains("event.key === \"Enter\"", script);
        Assert.Contains("select2:select", script);
        Assert.Contains("rw-outside-accept", script);
        Assert.Contains("outside/${lineId}", script);
        Assert.Contains("BrowserMultiFormatOneDReader", script);
        Assert.Contains("purchaseOrderLineId", script);
        Assert.Contains("@media(max-width:767.98px)", css);
    }

    [Fact]
    public void Unified_lookup_uses_readable_hierarchical_Select2_results()
    {
        var root = FindRoot();
        var script = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "wwwroot", "Admin", "js", "purchase-receiving-workbench.js"));
        var css = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "wwwroot", "Admin", "css", "purchase-receiving-workbench.css"));

        Assert.Contains("templateResult: renderLookupResult", script);
        Assert.Contains("templateSelection: renderLookupSelection", script);
        Assert.Contains("processResults: processLookupResults", script);
        Assert.Contains("rw-lookup-name", script);
        Assert.Contains("rw-lookup-unit", script);
        Assert.Contains("rw-lookup-factor", script);
        Assert.Contains("rw-lookup-barcode", script);
        Assert.Contains("Trong PO", script);
        Assert.Contains("Ngoài PO", script);
        Assert.Contains(".rw-lookup-name", css);
        Assert.Contains(".rw-lookup-meta", css);
    }

    [Fact]
    public void Unified_lookup_keyboard_loop_focuses_quantity_then_returns_to_search()
    {
        var script = File.ReadAllText(Path.Combine(
            FindRoot(), "GaoApp.Web", "wwwroot", "Admin", "js", "purchase-receiving-workbench.js"));

        Assert.Contains("$lookup.on(\"select2:select\"", script);
        Assert.Contains("receiveQuantity.value = \"1\";", script);
        Assert.Contains("receiveQuantity.focus();", script);
        Assert.Contains("receiveQuantity.select();", script);
        Assert.Contains("receiveQuantity?.addEventListener(\"keydown\"", script);
        Assert.Contains("await addSelectedQuantity();", script);
        Assert.Contains("focusUnifiedSearch();", script);
    }

    [Fact]
    public void Browser_never_calculates_authoritative_base_quantity()
    {
        var script = File.ReadAllText(Path.Combine(
            FindRoot(), "GaoApp.Web", "wwwroot", "Admin", "js", "purchase-receiving-workbench.js"));
        Assert.DoesNotContain("quantity * factor", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("baseQuantity =", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("item.currentReceiptBaseQuantity", script);
    }

    [Fact]
    public void Receiving_surfaces_use_authoritative_base_unit_names_without_redundant_equivalence()
    {
        var root = FindRoot();
        var dto = File.ReadAllText(Path.Combine(root, "GaoApp.Application", "DTOs", "Purchases", "PurchaseReceivingWorkbenchDtos.cs"));
        var service = File.ReadAllText(Path.Combine(root, "GaoApp.Application", "Services", "Purchases", "PurchaseReceivingWorkbenchService.cs"));
        var view = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "Areas", "Admin", "Views", "PurchaseReceiving", "Index.cshtml"));
        var script = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "wwwroot", "Admin", "js", "purchase-receiving-workbench.js"));

        Assert.Contains("string BaseUnitName", dto);
        Assert.Contains("bool IsBaseUnit", dto);
        Assert.Contains("BaseUnitName = x.ProductVariant.Product.BaseUnit.Name", service);
        Assert.Contains("IsBaseUnit = x.UnitId == x.ProductVariant.Product.BaseUnitId", service);
        Assert.Contains("@item.BaseUnitName", view);
        Assert.Contains("@if (!line.IsBaseUnit)", view);
        Assert.Contains("item.baseUnitName", script);
        Assert.Contains("line.isBaseUnit", script);
        Assert.DoesNotContain(" base</span>", view);
        Assert.DoesNotContain(" base</span>", script);
        Assert.DoesNotContain(" đơn vị gốc", script);
    }

    [Fact]
    public void Direct_receiving_uses_authoritative_base_unit_names()
    {
        var root = FindRoot();
        var lookupDto = File.ReadAllText(Path.Combine(root, "GaoApp.Application", "DTOs", "Inventory", "StockDocumentLookupSelect2ItemDto.cs"));
        var lineDto = File.ReadAllText(Path.Combine(root, "GaoApp.Application", "DTOs", "Inventory", "StockDocumentLineDto.cs"));
        var detail = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "Areas", "Admin", "Views", "WarehouseReceiving", "Detail.cshtml"));
        var lines = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "Areas", "Admin", "Views", "WarehouseReceiving", "_ReceivingLinesTable.cshtml"));
        var script = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "wwwroot", "Admin", "js", "warehouse-receiving-detail.js"));

        Assert.Contains("string BaseUnitName", lookupDto);
        Assert.Contains("bool IsBaseUnit", lookupDto);
        Assert.Contains("string BaseUnitName", lineDto);
        Assert.Contains("bool IsBaseUnit", lineDto);
        Assert.Contains("id=\"popupBaseQuantityBox\"", detail);
        Assert.Contains("@line.BaseUnitName", lines);
        Assert.Contains("if (selected?.isBaseUnit)", script);
        Assert.Contains("selected?.baseUnitName", script);
        Assert.DoesNotContain("SL gốc dự kiến", detail);
        Assert.DoesNotContain("SL gốc:", lines);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
