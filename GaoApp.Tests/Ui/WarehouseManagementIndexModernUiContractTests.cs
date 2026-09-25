using Xunit;

namespace GaoApp.Tests.Ui;

public sealed class WarehouseManagementIndexModernUiContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string IndexPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "Areas",
        "Admin",
        "Views",
        "WarehouseManagement",
        "Index.cshtml");

    private static readonly string StylePath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "wwwroot",
        "Admin",
        "css",
        "warehouse-management.css");

    private static readonly string ScriptPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "wwwroot",
        "Admin",
        "js",
        "warehouse-management.js");

    [Fact]
    public void Index_should_expose_the_approved_fluid_hierarchy_and_preserve_modal_contract()
    {
        var view = File.ReadAllText(IndexPath);

        Assert.Contains("ViewData[\"container\"] = \"container-fluid\"", view, StringComparison.Ordinal);
        Assert.Contains("data-gao-ui=\"modern\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-warehouse-management-index", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("row-cols-xl-4", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"warehouseSearch\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"warehouseLegalEntityFilter\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"warehouseLifecycleFilter\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"warehousePageSize\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"warehouseMobileList\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"warehouseModal\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"warehouseForm\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"warehouseLegalEntityId\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"warehouseAllowNegativeInventory\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<style", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Index_interaction_should_support_no_diacritic_search_filters_paging_and_mobile_cards()
    {
        var script = File.ReadAllText(ScriptPath);

        Assert.Contains("normalizeWarehouseSearchText", script, StringComparison.Ordinal);
        Assert.Contains("bindWarehouseIndexKpiFilters", script, StringComparison.Ordinal);
        Assert.Contains("applyWarehouseIndexFilters", script, StringComparison.Ordinal);
        Assert.Contains("renderWarehouseMobileCards", script, StringComparison.Ordinal);
        Assert.Contains("data-kpi-filter", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("addEventListener(\"input\"", script, StringComparison.Ordinal);
        Assert.Contains("warehousePageSize", script, StringComparison.Ordinal);
        Assert.Contains("WarehousePage.confirmSetDefault", script, StringComparison.Ordinal);
        Assert.Contains("WarehousePage.confirmToggleNegative", script, StringComparison.Ordinal);
        Assert.Contains("WarehousePage.confirmToggleActive", script, StringComparison.Ordinal);
        Assert.DoesNotContain("#${x.id}", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Index_styles_should_support_kpi_filters_desktop_table_and_mobile_cards()
    {
        var style = File.ReadAllText(StylePath);

        Assert.Contains(".warehouse-kpi-filter", style, StringComparison.Ordinal);
        Assert.Contains(".warehouse-desktop-list", style, StringComparison.Ordinal);
        Assert.Contains(".warehouse-mobile-list", style, StringComparison.Ordinal);
        Assert.Contains(".warehouse-mobile-card", style, StringComparison.Ordinal);
        Assert.Contains(".warehouse-config-controls", style, StringComparison.Ordinal);
        Assert.Contains("@media", style, StringComparison.OrdinalIgnoreCase);
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
