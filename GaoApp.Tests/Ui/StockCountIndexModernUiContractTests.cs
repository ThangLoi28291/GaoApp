using Xunit;

namespace GaoApp.Tests.Ui;

public sealed class StockCountIndexModernUiContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Index_should_expose_the_approved_table_mobile_cards_filters_and_quick_view()
    {
        var view = Read("GaoApp.Web", "Areas", "Admin", "Views", "StockCountPages", "Index.cshtml");

        Assert.Contains("ViewData[\"container\"] = \"container-fluid\"", view, StringComparison.Ordinal);
        Assert.Contains("data-gao-ui=\"modern\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-stock-count-index", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("row-cols-xl-4", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"scIndexKeyword\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"scIndexWarehouse\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"scIndexStatus\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"scIndexPageSize\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"stockCountDesktopList\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"stockCountMobileList\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"stockCountMobileFilterSheet\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"stockCountQuickViewModal\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ID:", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Index_interaction_should_use_latest_request_realtime_search_and_circular_paging()
    {
        var script = Read("GaoApp.Web", "wwwroot", "Admin", "js", "stock-count.js");

        Assert.Contains("AbortController", script, StringComparison.Ordinal);
        Assert.Contains("stockCountIndexRequestSequence", script, StringComparison.Ordinal);
        Assert.Contains("stockCountIndexSearchTimer", script, StringComparison.Ordinal);
        Assert.Contains("renderStockCountDesktopRows", script, StringComparison.Ordinal);
        Assert.Contains("renderStockCountMobileCards", script, StringComparison.Ordinal);
        Assert.Contains("renderStockCountCircularPagination", script, StringComparison.Ordinal);
        Assert.Contains("openStockCountIndexQuickView", script, StringComparison.Ordinal);
        Assert.Contains("stockCountIndexImageHoverPreview", script, StringComparison.Ordinal);
        Assert.Contains("pointerenter", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dblclick", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-page", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("350", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Index_styles_should_scope_mobile_cards_sheets_and_viewport_clearance()
    {
        var style = Read("GaoApp.Web", "wwwroot", "Admin", "css", "stock-count.css");

        Assert.Contains(".stock-count-index-page.gds-page", style, StringComparison.Ordinal);
        Assert.Contains(".sc-index-desktop-list", style, StringComparison.Ordinal);
        Assert.Contains(".sc-index-mobile-list", style, StringComparison.Ordinal);
        Assert.Contains(".sc-index-mobile-card", style, StringComparison.Ordinal);
        Assert.Contains(".sc-index-filter-sheet", style, StringComparison.Ordinal);
        Assert.Contains(".sc-index-image-hover-preview", style, StringComparison.Ordinal);
        Assert.Contains("overflow-x: hidden", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("env(safe-area-inset-bottom)", style, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Page_controller_should_use_only_the_isolated_view_authorized_read_service()
    {
        var controller = Read("GaoApp.Web", "Areas", "Admin", "Controllers", "StockCountPagesController.cs");

        Assert.Contains("[Authorize(Policy = PermissionCodes.Inventory.StockCount.View)]", controller, StringComparison.Ordinal);
        Assert.Contains("IStockCountIndexReadService", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"data\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"warehouse-options\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"quick-view\")]", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChanges", controller, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_repository_should_apply_exact_scope_images_and_no_write_behavior()
    {
        var repository = Read("GaoApp.Infrastructure", "Repositories", "Inventory", "StockCountIndexReadRepository.cs");

        Assert.Contains("Latin1_General_100_CI_AI", repository, StringComparison.Ordinal);
        Assert.Contains("AsNoTracking", repository, StringComparison.Ordinal);
        Assert.Contains("StockCountDocumentStatus.Draft", repository, StringComparison.Ordinal);
        Assert.Contains("StockCountDocumentStatus.PendingApproval", repository, StringComparison.Ordinal);
        Assert.Contains("StockCountDocumentStatus.Rejected", repository, StringComparison.Ordinal);
        Assert.Contains("StockCountDocumentStatus.Confirmed", repository, StringComparison.Ordinal);
        Assert.Contains("DifferenceQtyBase", repository, StringComparison.Ordinal);
        Assert.Contains("StoreId == storeId", repository, StringComparison.Ordinal);
        Assert.Contains("PrimaryProductImage", repository, StringComparison.Ordinal);
        Assert.Contains("ProductImages", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChanges", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ExecuteSql", repository, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine([RepositoryRoot, .. parts]));

    private static string FindRepositoryRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("GAOAPP_REPOSITORY_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot)
            && File.Exists(Path.Combine(configuredRoot, "GaoApp.sln")))
        {
            return configuredRoot;
        }

        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }
        throw new InvalidOperationException("Could not locate GaoApp repository root.");
    }
}
