using Xunit;

namespace GaoApp.Tests.Ui;

public sealed class InventoryAdjustmentIndexModernUiContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Index_should_expose_the_approved_adjustment_filters_table_and_mobile_sheets()
    {
        var view = Read("GaoApp.Web", "Areas", "Admin", "Views", "InventoryAdjustmentDocuments", "Index.cshtml");

        Assert.Contains("ViewData[\"container\"] = \"container-fluid\"", view, StringComparison.Ordinal);
        Assert.Contains("data-gao-ui=\"modern\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-inventory-adjustment-index", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("row-cols-xl-4", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"adjKeyword\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"adjWarehouse\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"adjType\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"adjReason\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"adjStatus\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"inventoryAdjustmentDesktopList\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"adjMobileList\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"inventoryAdjustmentMobileFilterSheet\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"inventoryAdjustmentQuickViewModal\"", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Index_interaction_should_use_latest_request_realtime_search_circular_paging_and_quick_view()
    {
        var script = Read("GaoApp.Web", "wwwroot", "Admin", "js", "inventory-adjustment-documents-index.js");

        Assert.Contains("AbortController", script, StringComparison.Ordinal);
        Assert.Contains("inventoryAdjustmentIndexRequestSequence", script, StringComparison.Ordinal);
        Assert.Contains("inventoryAdjustmentIndexSearchTimer", script, StringComparison.Ordinal);
        Assert.Contains("renderInventoryAdjustmentDesktopRows", script, StringComparison.Ordinal);
        Assert.Contains("renderInventoryAdjustmentMobileCards", script, StringComparison.Ordinal);
        Assert.Contains("renderInventoryAdjustmentCircularPagination", script, StringComparison.Ordinal);
        Assert.Contains("openInventoryAdjustmentQuickView", script, StringComparison.Ordinal);
        Assert.Contains("inventoryAdjustmentIndexImageHoverPreview", script, StringComparison.Ordinal);
        Assert.Contains("pointerenter", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dblclick", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-page", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("350", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Index_styles_should_scope_mobile_cards_sheets_and_viewport_clearance()
    {
        var style = Read("GaoApp.Web", "wwwroot", "Admin", "css", "inventory-adjustment-documents.css");

        Assert.Contains(".inventory-adjustment-index-page.gds-page", style, StringComparison.Ordinal);
        Assert.Contains(".adj-index-desktop-list", style, StringComparison.Ordinal);
        Assert.Contains(".adj-index-mobile-list", style, StringComparison.Ordinal);
        Assert.Contains(".adj-index-mobile-card", style, StringComparison.Ordinal);
        Assert.Contains(".adj-index-filter-sheet", style, StringComparison.Ordinal);
        Assert.Contains(".adj-index-image-hover-preview", style, StringComparison.Ordinal);
        Assert.Contains("overflow-x: hidden", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("env(safe-area-inset-bottom)", style, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Page_controller_should_use_only_the_isolated_view_authorized_read_service()
    {
        var controller = Read("GaoApp.Web", "Areas", "Admin", "Controllers", "InventoryAdjustmentDocumentsController.cs");

        Assert.Contains("[Authorize(Policy = PermissionCodes.Inventory.Adjustment.View)]", controller, StringComparison.Ordinal);
        Assert.Contains("IInventoryAdjustmentIndexReadService", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"data\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"warehouse-options\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"quick-view\")]", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChanges", controller, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_repository_should_apply_exact_scope_images_and_no_write_behavior()
    {
        var repository = Read("GaoApp.Infrastructure", "Repositories", "Inventory", "InventoryAdjustmentIndexReadRepository.cs");

        Assert.Contains("Latin1_General_100_CI_AI", repository, StringComparison.Ordinal);
        Assert.Contains("AsNoTracking", repository, StringComparison.Ordinal);
        Assert.Contains("InventoryAdjustmentDocumentStatus.Draft", repository, StringComparison.Ordinal);
        Assert.Contains("InventoryAdjustmentDocumentStatus.PendingApproval", repository, StringComparison.Ordinal);
        Assert.Contains("InventoryAdjustmentDocumentStatus.Rejected", repository, StringComparison.Ordinal);
        Assert.Contains("InventoryAdjustmentDocumentStatus.Approved", repository, StringComparison.Ordinal);
        Assert.Contains("AdjustmentType", repository, StringComparison.Ordinal);
        Assert.Contains("ReasonType", repository, StringComparison.Ordinal);
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
