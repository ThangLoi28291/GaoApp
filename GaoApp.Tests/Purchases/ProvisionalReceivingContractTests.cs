namespace GaoApp.Tests.Purchases;

public sealed class ProvisionalReceivingContractTests
{
    [Fact]
    public void Separate_provisional_aggregate_and_additive_migration_are_present()
    {
        var root = FindRoot();

        Assert.True(File.Exists(Path.Combine(root, "GaoApp.Domain", "Entities",
            "StockDocumentProvisionalItem.cs")));
        Assert.True(File.Exists(Path.Combine(root, "GaoApp.Domain", "Enums",
            "ProvisionalReceivingEnums.cs")));
        Assert.True(Directory.GetFiles(Path.Combine(root, "GaoApp.Infrastructure", "Migrations"),
            "*AddProvisionalReceivingItems.cs").Length == 1);

        var line = File.ReadAllText(Path.Combine(root, "GaoApp.Domain", "Entities",
            "StockDocumentLine.cs"));
        Assert.Contains("public int ProductVariantId", line);
        Assert.DoesNotContain("public int? ProductVariantId", line);
    }

    [Fact]
    public void Shared_api_confirm_guard_and_cross_workflow_editable_guard_are_present()
    {
        var root = FindRoot();
        var controllerPath = Path.Combine(root, "GaoApp.Web", "Areas", "Admin", "Controllers",
            "StockDocumentProvisionalItemsController.cs");
        Assert.True(File.Exists(controllerPath));

        var stockService = File.ReadAllText(Path.Combine(root, "GaoApp.Application", "Services",
            "Inventory", "StockDocumentService.cs"));
        var workbenchRepository = File.ReadAllText(Path.Combine(root, "GaoApp.Infrastructure",
            "Repositories", "Purchases", "PurchaseReceivingWorkbenchRepository.cs"));

        Assert.Contains("UNRESOLVED_PROVISIONAL", stockService);
        Assert.Contains("GetEditableForPurchaseOrderAsync", workbenchRepository);
    }

    [Fact]
    public void Feature_switch_is_disabled_by_default_and_both_warehouse_surfaces_offer_capture()
    {
        var root = FindRoot();
        var settings = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "appsettings.json"));
        var workbench = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "Areas", "Admin",
            "Views", "PurchaseReceiving", "Index.cshtml"));
        var manager = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "Areas", "Admin",
            "Views", "StockDocumentManagement", "Edit.cshtml"));

        Assert.Contains("\"ProvisionalItemsEnabled\": false", settings);
        Assert.Contains("Ghi nhận hàng mới", workbench);
        Assert.Contains("Sản phẩm chưa có trong danh mục", manager);
        Assert.Contains("Tạo sản phẩm mới", manager);
        Assert.Contains("Liên kết sản phẩm có sẵn", manager);
    }

    [Fact]
    public void Receipt_scoped_ui_exposes_capture_manager_resolution_and_server_authoritative_state()
    {
        var root = FindRoot();
        var script = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "wwwroot", "Admin", "js",
            "provisional-receiving.js"));
        var controller = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "Areas", "Admin",
            "Controllers", "StockDocumentProvisionalItemsController.cs"));

        Assert.Contains("/provisional-items", script);
        Assert.Contains("provisional:state", script);
        Assert.Contains("/quick-create", script);
        Assert.Contains("/link", script);
        Assert.Contains("/remove", script);
        Assert.Contains("rememberRawBarcode", script);
        Assert.DoesNotContain("baseQuantity", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ReceiptSource", controller);
        Assert.Contains("PermissionCodes.Purchase.Receipt.Update", controller);
        Assert.Contains("PermissionCodes.Inventory.StockDocument.Update", controller);
        Assert.Contains("Catalog.Product.Create", controller);
        Assert.Contains("Catalog.Unit.Create", controller);
        Assert.Contains("Catalog.Barcode.Create", controller);
        Assert.Contains("duplicateCandidateCount", controller);

        var dto = File.ReadAllText(Path.Combine(root, "GaoApp.Application", "DTOs", "Purchases",
            "ProvisionalReceivingDtos.cs"));
        Assert.Contains("PermittedActions", dto);
        Assert.Contains("BarcodeState", dto);
        Assert.Contains("ResolutionOutcome", dto);
        Assert.Contains("ResolutionAllocationKind", dto);
        Assert.Contains("provisional:capture-request", script);
        Assert.Contains("provisional:saved", script);

        var workbenchScript = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "wwwroot", "Admin", "js",
            "purchase-receiving-workbench.js"));
        Assert.Contains("Không tìm thấy sản phẩm", workbenchScript);
        Assert.Contains("Ghi nhận hàng mới", workbenchScript);
        Assert.Contains("provisional:capture-request", workbenchScript);
    }

    [Fact]
    public void Quick_create_uses_transaction_free_catalog_core_under_receipt_transaction()
    {
        var root = FindRoot();
        var catalog = File.ReadAllText(Path.Combine(root, "GaoApp.Application", "Services",
            "Purchases", "ProcurementCatalogService.cs"));
        var provisional = File.ReadAllText(Path.Combine(root, "GaoApp.Application", "Services",
            "Purchases", "StockDocumentProvisionalItemService.cs"));

        Assert.Contains("CreateProductWithinTransactionAsync", catalog);
        Assert.Contains("return CreateProductCoreAsync", catalog);
        Assert.Contains("CreateProductWithinTransactionAsync", provisional);
        Assert.Contains("WithLockedDocumentAsync", provisional);
        Assert.Contains("[BARCODE_CONFLICT]", provisional);
        Assert.Contains("[IDEMPOTENCY_CONFLICT]", provisional);
        Assert.Contains("RawBarcodeRemembered", provisional);
    }

    [Fact]
    public void Unknown_unified_input_is_classified_and_prefilled_for_po_and_direct_receipts()
    {
        var root = FindRoot();
        var provisional = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "wwwroot", "Admin", "js",
            "provisional-receiving.js"));
        var workbench = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "wwwroot", "Admin", "js",
            "purchase-receiving-workbench.js"));
        var direct = File.ReadAllText(Path.Combine(root, "GaoApp.Web", "wwwroot", "Admin", "js",
            "stock-document-management.js"));

        Assert.Contains("classifyUnknownInput", provisional);
        Assert.Contains("barcodeLike", provisional);
        Assert.Contains("prefill.name", provisional);
        Assert.Contains("prefill.rawBarcode", provisional);
        Assert.Contains("detail?.rawInput", provisional);

        Assert.Contains("detail: { rawInput: term }", workbench);
        Assert.DoesNotContain("detail: { rawBarcode: term }", workbench);

        Assert.Contains("showUnknownProvisionalCapture", direct);
        Assert.Contains("Ghi nhận hàng mới", direct);
        Assert.Contains("detail: { rawInput: term }", direct);

        Assert.DoesNotContain("SaveBarcode", workbench);
        Assert.DoesNotContain("SaveBarcode", direct);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
