using Xunit;

namespace GaoApp.Tests.Ui;

public sealed class PurchaseOrderIndexModernUiContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string IndexPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "Areas", "Admin", "Views", "PurchaseOrders", "Index.cshtml");

    private static readonly string ScriptPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "wwwroot", "Admin", "js", "purchase-order-index.js");

    private static readonly string StylePath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "wwwroot", "Admin", "css", "purchase-order-index.css");

    private static readonly string ControllerPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "Areas", "Admin", "Controllers", "PurchaseOrdersController.cs");

    private static readonly string RepositoryPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Infrastructure", "Repositories", "Purchases", "PurchaseOrderIndexReadRepository.cs");

    private static readonly string DtoPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Application", "DTOs", "Purchases", "PurchaseOrderIndexReadDtos.cs");

    private static readonly string ServicePath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Application", "Services", "Purchases", "PurchaseOrderIndexReadService.cs");

    [Fact]
    public void Index_should_expose_approved_kpis_filters_table_and_mobile_cards()
    {
        var view = File.ReadAllText(IndexPath);

        Assert.Contains("ViewData[\"container\"] = \"container-fluid\"", view, StringComparison.Ordinal);
        Assert.Contains("data-gao-ui=\"modern\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-purchase-order-index", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("row-cols-xl-4", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"poKeyword\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"poLegalEntity\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"poSupplier\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"poWarehouse\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"poSource\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"poState\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"poFromDate\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"poToDate\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"poPageSize\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"purchaseOrderDesktopList\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"purchaseOrderMobileList\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"purchaseOrderMobileFilterSheet\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"purchaseOrderQuickViewModal\"", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Index_interaction_should_use_latest_request_line_progress_and_permission_safe_cost()
    {
        var script = File.ReadAllText(ScriptPath);

        Assert.Contains("AbortController", script, StringComparison.Ordinal);
        Assert.Contains("purchaseOrderIndexRequestSequence", script, StringComparison.Ordinal);
        Assert.Contains("purchaseOrderIndexSearchTimer", script, StringComparison.Ordinal);
        Assert.Contains("renderPurchaseOrderDesktopRows", script, StringComparison.Ordinal);
        Assert.Contains("renderPurchaseOrderMobileCards", script, StringComparison.Ordinal);
        Assert.Contains("renderPurchaseOrderCircularPagination", script, StringComparison.Ordinal);
        Assert.Contains("openPurchaseOrderQuickView", script, StringComparison.Ordinal);
        Assert.Contains("purchaseOrderIndexImageHoverPreview", script, StringComparison.Ordinal);
        Assert.Contains("resolvedLineCount", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("eligibleLineCount", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("canViewCost", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dblclick", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Module_styles_should_scope_cards_sheets_image_preview_and_mobile_safe_area()
    {
        var style = File.ReadAllText(StylePath);

        Assert.Contains(".purchase-order-index-page.gds-page", style, StringComparison.Ordinal);
        Assert.Contains(".po-index-desktop-list", style, StringComparison.Ordinal);
        Assert.Contains(".po-index-mobile-list", style, StringComparison.Ordinal);
        Assert.Contains(".po-index-mobile-card", style, StringComparison.Ordinal);
        Assert.Contains(".po-index-filter-sheet", style, StringComparison.Ordinal);
        Assert.Contains(".po-index-image-hover-preview", style, StringComparison.Ordinal);
        Assert.Contains("overflow-x: hidden", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("env(safe-area-inset-bottom)", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@media", style, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Page_controller_should_preserve_access_and_use_get_only_index_service()
    {
        var controller = File.ReadAllText(ControllerPath);

        Assert.Contains("IPurchaseOrderIndexReadService", controller, StringComparison.Ordinal);
        Assert.Contains("CanAccessOrdersAsync", controller, StringComparison.Ordinal);
        Assert.Contains("PermissionCodes.Purchase.Order.ViewCost", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"data\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"filter-options\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"quick-view\")]", controller, StringComparison.Ordinal);
        Assert.Contains("GetPurchaseOrderIndexData", controller, StringComparison.Ordinal);
        Assert.Contains("GetPurchaseOrderQuickView", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_stack_should_apply_exact_scope_search_progress_cost_and_no_write_behavior()
    {
        var repository = File.ReadAllText(RepositoryPath);
        var dto = File.ReadAllText(DtoPath);
        var service = File.ReadAllText(ServicePath);

        Assert.Contains("Latin1_General_100_CI_AI", repository, StringComparison.Ordinal);
        Assert.Contains("AsNoTracking", repository, StringComparison.Ordinal);
        Assert.Contains("StoreId == storeId", repository, StringComparison.Ordinal);
        Assert.Contains("SourcePurchaseRequest", repository, StringComparison.Ordinal);
        Assert.Contains("ProductNameSnapshot", repository, StringComparison.Ordinal);
        Assert.Contains("SkuSnapshot", repository, StringComparison.Ordinal);
        Assert.Contains("ProductVariantUnitBarcodes", repository, StringComparison.Ordinal);
        Assert.Contains("EligibleLineCount", repository, StringComparison.Ordinal);
        Assert.Contains("ResolvedLineCount", repository, StringComparison.Ordinal);
        Assert.Contains("ShortClosedQuantity", repository, StringComparison.Ordinal);
        Assert.Contains("PrimaryProductImage", repository, StringComparison.Ordinal);
        Assert.Contains("IncludeCost", repository, StringComparison.Ordinal);
        Assert.Contains("PurchaseOrderIndexStates.Open", service, StringComparison.Ordinal);
        Assert.Contains("PurchaseOrderIndexStates.NeedsAction", service, StringComparison.Ordinal);
        Assert.Contains("PurchaseOrderIndexStates.InProgress", service, StringComparison.Ordinal);
        Assert.Contains("PurchaseOrderIndexStates.Completed", service, StringComparison.Ordinal);
        Assert.Contains("CanViewCost", dto, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChanges", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ExecuteSql", repository, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var configured = Environment.GetEnvironmentVariable("GAOAPP_REPOSITORY_ROOT");
        if (!string.IsNullOrWhiteSpace(configured)
            && File.Exists(Path.Combine(configured, "GaoApp.sln")))
        {
            return configured;
        }

        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
                    return directory.FullName;

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException(
            "Could not locate GaoApp repository root from test output directory.");
    }
}
