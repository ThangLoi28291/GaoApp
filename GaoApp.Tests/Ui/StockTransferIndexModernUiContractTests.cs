using Xunit;

namespace GaoApp.Tests.Ui;

public sealed class StockTransferIndexModernUiContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string IndexPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "Areas", "Admin", "Views", "StockTransfer", "Index.cshtml");

    private static readonly string ScriptPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "wwwroot", "Admin", "js", "stock-transfer.js");

    private static readonly string StylePath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "wwwroot", "Admin", "css", "stock-transfer.css");

    private static readonly string ControllerPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "Areas", "Admin", "Controllers", "StockTransferController.cs");

    private static readonly string RepositoryPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Infrastructure", "Repositories", "Inventory", "StockTransferIndexReadRepository.cs");

    [Fact]
    public void Index_should_expose_the_approved_route_table_and_explicit_mobile_cards()
    {
        var view = File.ReadAllText(IndexPath);

        Assert.Contains("ViewData[\"container\"] = \"container-fluid\"", view, StringComparison.Ordinal);
        Assert.Contains("data-gao-ui=\"modern\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-stock-transfer-index", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("row-cols-xl-4", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"stKeyword\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"stFromWarehouseFilter\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"stToWarehouseFilter\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"stStatus\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"stPageSize\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"stockTransferDesktopList\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"stockTransferMobileList\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"stockTransferMobileFilterSheet\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"stockTransferQuickDetailModal\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StockTransferDocumentId", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Index_interaction_should_support_latest_request_circular_paging_and_read_only_quick_view()
    {
        var script = File.ReadAllText(ScriptPath);

        Assert.Contains("AbortController", script, StringComparison.Ordinal);
        Assert.Contains("stockTransferIndexRequestSequence", script, StringComparison.Ordinal);
        Assert.Contains("stockTransferIndexSearchTimer", script, StringComparison.Ordinal);
        Assert.Contains("renderStockTransferDesktopRows", script, StringComparison.Ordinal);
        Assert.Contains("renderStockTransferMobileCards", script, StringComparison.Ordinal);
        Assert.Contains("renderStockTransferCircularPagination", script, StringComparison.Ordinal);
        Assert.Contains("openStockTransferQuickDetail", script, StringComparison.Ordinal);
        Assert.Contains("stockTransferIndexImageHoverPreview", script, StringComparison.Ordinal);
        Assert.Contains("pointerenter", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dblclick", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-page", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Nháp + Chờ duyệt", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Module_styles_should_scope_the_modern_index_cards_sheets_and_mobile_safe_area()
    {
        var style = File.ReadAllText(StylePath);

        Assert.Contains(".stock-transfer-index-page.gds-page", style, StringComparison.Ordinal);
        Assert.Contains(".st-index-desktop-list", style, StringComparison.Ordinal);
        Assert.Contains(".st-index-mobile-list", style, StringComparison.Ordinal);
        Assert.Contains(".st-index-mobile-card", style, StringComparison.Ordinal);
        Assert.Contains(".st-index-filter-sheet", style, StringComparison.Ordinal);
        Assert.Contains(".st-index-image-hover-preview", style, StringComparison.Ordinal);
        Assert.Contains("overflow-x: hidden", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("env(safe-area-inset-bottom)", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@media", style, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Page_controller_should_use_only_the_isolated_authenticated_read_service()
    {
        var controller = File.ReadAllText(ControllerPath);

        Assert.Contains("[Authorize(Policy = PermissionCodes.Inventory.StockTransfer.View)]", controller, StringComparison.Ordinal);
        Assert.Contains("IStockTransferIndexReadService", controller, StringComparison.Ordinal);
        Assert.Contains("GetData", controller, StringComparison.Ordinal);
        Assert.Contains("GetQuickView", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"data\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"quick-view\")]", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChanges", controller, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_repository_should_apply_exact_scope_tenant_filters_and_no_write_behavior()
    {
        var repository = File.ReadAllText(RepositoryPath);

        Assert.Contains("Latin1_General_100_CI_AI", repository, StringComparison.Ordinal);
        Assert.Contains("AsNoTracking", repository, StringComparison.Ordinal);
        Assert.Contains("StockTransferDocumentStatus.Draft", repository, StringComparison.Ordinal);
        Assert.Contains("StockTransferDocumentStatus.PendingApproval", repository, StringComparison.Ordinal);
        Assert.Contains("StockTransferDocumentStatus.Rejected", repository, StringComparison.Ordinal);
        Assert.Contains("StockTransferDocumentStatus.Confirmed", repository, StringComparison.Ordinal);
        Assert.Contains("StoreId == storeId", repository, StringComparison.Ordinal);
        Assert.Contains("FromWarehouse", repository, StringComparison.Ordinal);
        Assert.Contains("ToWarehouse", repository, StringComparison.Ordinal);
        Assert.Contains("PrimaryProductImage", repository, StringComparison.Ordinal);
        Assert.Contains("ProductImages", repository, StringComparison.Ordinal);
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
