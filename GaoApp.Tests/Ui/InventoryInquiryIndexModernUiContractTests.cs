using Xunit;

namespace GaoApp.Tests.Ui;

public sealed class InventoryInquiryIndexModernUiContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string IndexPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "Areas",
        "Admin",
        "Views",
        "InventoryInquiry",
        "Index.cshtml");

    private static readonly string ScriptPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "wwwroot",
        "Admin",
        "js",
        "inventory-inquiry.js");

    private static readonly string StylePath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "wwwroot",
        "Admin",
        "css",
        "inventory-inquiry.css");

    private static readonly string ControllerPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "Areas",
        "Admin",
        "Controllers",
        "InventoryInquiryController.cs");

    private static readonly string RepositoryPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Infrastructure",
        "Repositories",
        "Inventory",
        "InventoryInquiryReadRepository.cs");

    [Fact]
    public void Index_should_expose_the_approved_read_only_desktop_and_mobile_hierarchy()
    {
        var view = File.ReadAllText(IndexPath);

        Assert.Contains("ViewData[\"container\"] = \"container-fluid\"", view, StringComparison.Ordinal);
        Assert.Contains("data-gao-ui=\"modern\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-inventory-inquiry-index", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("row-cols-xl-4", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"inventoryKeyword\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"inventoryWarehouseFilter\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"inventoryStateFilter\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"inventoryPageSize\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"inventoryDesktopList\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"inventoryMobileList\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"inventoryQuickViewModal\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"inventoryMobileFilterSheet\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("~/Admin/css/inventory-inquiry.css", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("id=\"ProductVariantId\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<style", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Interaction_should_support_latest_request_search_images_quick_view_and_circular_paging()
    {
        var script = File.ReadAllText(ScriptPath);

        Assert.Contains("AbortController", script, StringComparison.Ordinal);
        Assert.Contains("requestSequence", script, StringComparison.Ordinal);
        Assert.Contains("searchDebounceTimer", script, StringComparison.Ordinal);
        Assert.Contains("renderInventoryDesktopRows", script, StringComparison.Ordinal);
        Assert.Contains("renderInventoryMobileCards", script, StringComparison.Ordinal);
        Assert.Contains("renderInventoryPagination", script, StringComparison.Ordinal);
        Assert.Contains("openInventoryQuickView", script, StringComparison.Ordinal);
        Assert.Contains("inventoryImageHoverPreview", script, StringComparison.Ordinal);
        Assert.Contains("pointerenter", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dblclick", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-page", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("navigator.clipboard", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("item.sku", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Module_styles_should_provide_table_cards_filter_sheet_and_mobile_safe_area()
    {
        var style = File.ReadAllText(StylePath);

        Assert.Contains(".inventory-desktop-list", style, StringComparison.Ordinal);
        Assert.Contains(".inventory-mobile-list", style, StringComparison.Ordinal);
        Assert.Contains(".inventory-mobile-card", style, StringComparison.Ordinal);
        Assert.Contains(".inventory-filter-sheet", style, StringComparison.Ordinal);
        Assert.Contains(".inventory-image-hover-preview", style, StringComparison.Ordinal);
        Assert.Contains(".inventory-inquiry-page.gds-page", style, StringComparison.Ordinal);
        Assert.Contains("overflow-x: hidden", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".inventory-unit-table td::before", style, StringComparison.Ordinal);
        Assert.Contains("env(safe-area-inset-bottom)", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@media", style, StringComparison.OrdinalIgnoreCase);

        var script = File.ReadAllText(ScriptPath);
        Assert.Contains("data-label=\"Đơn vị\"", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-label=\"Giá bán\"", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Controller_should_use_the_isolated_authenticated_read_service()
    {
        var controller = File.ReadAllText(ControllerPath);

        Assert.Contains("[Authorize]", controller, StringComparison.Ordinal);
        Assert.Contains("IInventoryInquiryReadService", controller, StringComparison.Ordinal);
        Assert.Contains("GetData", controller, StringComparison.Ordinal);
        Assert.Contains("GetQuickView", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"data\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"quick-view\")]", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChanges", controller, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_repository_should_preserve_inventory_math_tenant_filters_and_price_precedence()
    {
        var repository = File.ReadAllText(RepositoryPath);

        Assert.Contains("Latin1_General_100_CI_AI", repository, StringComparison.Ordinal);
        Assert.Contains("AsNoTracking", repository, StringComparison.Ordinal);
        Assert.Contains("OnHandQty < 0", repository, StringComparison.Ordinal);
        Assert.Contains("OnHandQty == 0", repository, StringComparison.Ordinal);
        Assert.Contains("OnHandQty > 0", repository, StringComparison.Ordinal);
        Assert.Contains("conversion.Price ?? variant.Price ?? product.BasePrice", repository, StringComparison.Ordinal);
        Assert.Contains("PrimaryProductImage", repository, StringComparison.Ordinal);
        Assert.Contains("ProductImages", repository, StringComparison.Ordinal);
        Assert.Contains("!conversion.IsDeleted && conversion.IsActive", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChanges", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ExecuteSql", repository, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate GaoApp repository root from test output directory.");
    }
}
