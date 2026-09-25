using Xunit;

namespace GaoApp.Tests.Ui;

public sealed class PurchaseRequestIndexModernUiContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string IndexPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "Areas", "Admin", "Views", "PurchaseRequests", "Index.cshtml");

    private static readonly string ScriptPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "wwwroot", "Admin", "js", "purchase-request-index.js");

    private static readonly string StylePath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "wwwroot", "Admin", "css", "purchase-request-index.css");

    private static readonly string ControllerPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "Areas", "Admin", "Controllers", "PurchaseRequestsController.cs");

    private static readonly string RepositoryPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Infrastructure", "Repositories", "Purchases", "PurchaseRequestIndexReadRepository.cs");

    private static readonly string DtoPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Application", "DTOs", "Purchases", "PurchaseRequestIndexReadDtos.cs");

    [Fact]
    public void Index_should_expose_approved_scope_filters_table_and_mobile_cards()
    {
        var view = File.ReadAllText(IndexPath);

        Assert.Contains("ViewData[\"container\"] = \"container-fluid\"", view, StringComparison.Ordinal);
        Assert.Contains("data-gao-ui=\"modern\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-purchase-request-index", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("row-cols-xl-4", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"prScopeStore\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"prScopeMine\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"prKeyword\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"prRequester\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"prStatus\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"prFromDate\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"prToDate\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"prPageSize\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"purchaseRequestDesktopList\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"purchaseRequestMobileList\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"purchaseRequestMobileFilterSheet\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"purchaseRequestQuickViewModal\"", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Index_interaction_should_use_latest_request_line_progress_and_no_cost_quick_view()
    {
        var script = File.ReadAllText(ScriptPath);

        Assert.Contains("AbortController", script, StringComparison.Ordinal);
        Assert.Contains("purchaseRequestIndexRequestSequence", script, StringComparison.Ordinal);
        Assert.Contains("purchaseRequestIndexSearchTimer", script, StringComparison.Ordinal);
        Assert.Contains("renderPurchaseRequestDesktopRows", script, StringComparison.Ordinal);
        Assert.Contains("renderPurchaseRequestMobileCards", script, StringComparison.Ordinal);
        Assert.Contains("renderPurchaseRequestCircularPagination", script, StringComparison.Ordinal);
        Assert.Contains("openPurchaseRequestQuickView", script, StringComparison.Ordinal);
        Assert.Contains("purchaseRequestIndexImageHoverPreview", script, StringComparison.Ordinal);
        Assert.Contains("completedLineCount", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("eligibleLineCount", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dblclick", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unitPrice", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("totalCost", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Module_styles_should_scope_cards_sheets_image_preview_and_mobile_safe_area()
    {
        var style = File.ReadAllText(StylePath);

        Assert.Contains(".purchase-request-index-page.gds-page", style, StringComparison.Ordinal);
        Assert.Contains(".pr-index-desktop-list", style, StringComparison.Ordinal);
        Assert.Contains(".pr-index-mobile-list", style, StringComparison.Ordinal);
        Assert.Contains(".pr-index-mobile-card", style, StringComparison.Ordinal);
        Assert.Contains(".pr-index-filter-sheet", style, StringComparison.Ordinal);
        Assert.Contains(".pr-index-image-hover-preview", style, StringComparison.Ordinal);
        Assert.Contains("overflow-x: hidden", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("env(safe-area-inset-bottom)", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@media", style, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Page_controller_should_keep_row_authorization_and_use_get_only_index_service()
    {
        var controller = File.ReadAllText(ControllerPath);

        Assert.Contains("IPurchaseRequestIndexReadService", controller, StringComparison.Ordinal);
        Assert.Contains("PermissionCodes.Purchase.Request.ViewOwn", controller, StringComparison.Ordinal);
        Assert.Contains("PermissionCodes.Purchase.Request.ViewStore", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"data\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"requester-options\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"quick-view\")]", controller, StringComparison.Ordinal);
        Assert.Contains("GetPurchaseRequestIndexData", controller, StringComparison.Ordinal);
        Assert.Contains("GetPurchaseRequestQuickView", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_repository_should_apply_exact_scope_search_progress_and_no_write_behavior()
    {
        var repository = File.ReadAllText(RepositoryPath);
        var dto = File.ReadAllText(DtoPath);

        Assert.Contains("Latin1_General_100_CI_AI", repository, StringComparison.Ordinal);
        Assert.Contains("AsNoTracking", repository, StringComparison.Ordinal);
        Assert.Contains("StoreId == storeId", repository, StringComparison.Ordinal);
        Assert.Contains("RequestedByUserId", repository, StringComparison.Ordinal);
        Assert.Contains("ProductNameSnapshot", repository, StringComparison.Ordinal);
        Assert.Contains("SkuSnapshot", repository, StringComparison.Ordinal);
        Assert.Contains("ProductVariantUnitBarcodes", repository, StringComparison.Ordinal);
        Assert.Contains("EligibleLineCount", repository, StringComparison.Ordinal);
        Assert.Contains("CompletedLineCount", repository, StringComparison.Ordinal);
        Assert.Contains("PrimaryProductImage", repository, StringComparison.Ordinal);
        Assert.Contains("InventoryBalances", repository, StringComparison.Ordinal);
        Assert.Contains("PurchaseOrderLines", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChanges", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ExecuteSql", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Price", dto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Cost", dto, StringComparison.OrdinalIgnoreCase);
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
