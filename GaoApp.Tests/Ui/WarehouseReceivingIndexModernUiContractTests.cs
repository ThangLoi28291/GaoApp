using Xunit;

namespace GaoApp.Tests.Ui;

public sealed class WarehouseReceivingIndexModernUiContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string IndexPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "Areas",
        "Admin",
        "Views",
        "WarehouseReceiving",
        "Index.cshtml");

    private static readonly string DetailPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "Areas",
        "Admin",
        "Views",
        "WarehouseReceiving",
        "Detail.cshtml");

    private static readonly string StylePath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "wwwroot",
        "Admin",
        "css",
        "warehouse-receiving.css");

    private static readonly string ScriptPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "wwwroot",
        "Admin",
        "js",
        "warehouse-receiving.js");

    [Fact]
    public void Index_should_expose_the_approved_fast_receiving_hierarchy()
    {
        var view = File.ReadAllText(IndexPath);

        Assert.Contains("ViewData[\"Title\"] = \"Nhập hàng nhanh\"", view, StringComparison.Ordinal);
        Assert.Contains("ViewData[\"container\"] = \"container-fluid\"", view, StringComparison.Ordinal);
        Assert.Contains("data-gao-ui=\"modern\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-warehouse-receiving-index", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("row-cols-xl-3", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"wrSearch\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"wrReceiptList\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Tạo phiếu nhập nhanh", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"wrCreateModal\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"wrDirectReceiptSource\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"wrLegalEntityId\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"wrWarehouseId\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<style", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Index_interaction_should_filter_kpis_keep_realtime_search_and_use_status_specific_ctas()
    {
        var script = File.ReadAllText(ScriptPath);

        Assert.Contains("bindWarehouseReceivingKpiFilters", script, StringComparison.Ordinal);
        Assert.Contains("data-status-filter", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("addEventListener('input'", script, StringComparison.Ordinal);
        Assert.Contains("Tiếp tục nhập", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Xem phiếu", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sửa phiếu", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("· #${x.id}", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Index_styles_should_support_task_cards_and_detail_workbench_should_remain_outside_opt_in()
    {
        var style = File.ReadAllText(StylePath);
        var detail = File.ReadAllText(DetailPath);

        Assert.Contains(".wr-kpi-filter", style, StringComparison.Ordinal);
        Assert.Contains(".wr-card-facts", style, StringComparison.Ordinal);
        Assert.Contains(".wr-card-action", style, StringComparison.Ordinal);
        Assert.Contains("@media", style, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data-warehouse-receiving-index", detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data-gao-ui=\"modern\"", detail, StringComparison.OrdinalIgnoreCase);
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
