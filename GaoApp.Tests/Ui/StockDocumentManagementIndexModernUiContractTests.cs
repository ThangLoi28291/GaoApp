using Xunit;

namespace GaoApp.Tests.Ui;

public sealed class StockDocumentManagementIndexModernUiContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string IndexPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "Areas",
        "Admin",
        "Views",
        "StockDocumentManagement",
        "Index.cshtml");

    private static readonly string EditPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "Areas",
        "Admin",
        "Views",
        "StockDocumentManagement",
        "Edit.cshtml");

    private static readonly string StylePath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "wwwroot",
        "Admin",
        "css",
        "stock-document-management.css");

    private static readonly string ScriptPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "wwwroot",
        "Admin",
        "js",
        "stock-document-management.js");

    [Fact]
    public void Index_should_opt_in_to_the_approved_manager_hierarchy()
    {
        var view = File.ReadAllText(IndexPath);

        Assert.Contains("ViewData[\"container\"] = \"container-fluid\"", view, StringComparison.Ordinal);
        Assert.Contains("data-gao-ui=\"modern\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-stock-document-index", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("row-cols-xl-5", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"sdWaitingInvoiceKpi\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Tra cứu phiếu nhập", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"sdSearchKeyword\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"sdStatusFilter\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"sdPageSize\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"sdReceiptMobileList\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"stockDocumentInfoModal\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<style", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Index_interaction_should_filter_kpis_render_mobile_cards_and_hide_internal_id()
    {
        var script = File.ReadAllText(ScriptPath);

        Assert.Contains("bindStockDocumentIndexKpiFilters", script, StringComparison.Ordinal);
        Assert.Contains("renderStockDocumentIndexMobileCards", script, StringComparison.Ordinal);
        Assert.Contains("data-status-filter", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("js-stock-document-quick-view", script, StringComparison.Ordinal);
        Assert.Contains("dblclick", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Tiến độ", File.ReadAllText(IndexPath), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HKD nhập hàng", File.ReadAllText(IndexPath), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("createLegalEntityId", File.ReadAllText(IndexPath), StringComparison.Ordinal);
        Assert.Contains("createWarehouseId", File.ReadAllText(IndexPath), StringComparison.Ordinal);
        Assert.Contains("Cập nhật", File.ReadAllText(IndexPath), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#${x.id}", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Index_styles_should_be_scoped_and_edit_workbench_should_remain_outside_modern_opt_in()
    {
        var style = File.ReadAllText(StylePath);
        var edit = File.ReadAllText(EditPath);

        Assert.Contains(".sd-index-kpi-filter", style, StringComparison.Ordinal);
        Assert.Contains(".sd-index-mobile-list", style, StringComparison.Ordinal);
        Assert.Contains(".sd-index-mobile-card", style, StringComparison.Ordinal);
        Assert.Contains("@media", style, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data-stock-document-index", edit, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data-gao-ui=\"modern\"", edit, StringComparison.OrdinalIgnoreCase);
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
