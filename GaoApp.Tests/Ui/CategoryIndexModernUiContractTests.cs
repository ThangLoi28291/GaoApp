using System.Text.RegularExpressions;
using Xunit;

namespace GaoApp.Tests.Ui;

public sealed class CategoryIndexModernUiContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string CategoryIndexPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "Areas",
        "Admin",
        "Views",
        "Category",
        "Index.cshtml");

    private static readonly string CategoryTablePath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "Areas",
        "Admin",
        "Views",
        "Category",
        "_CategoryTable.cshtml");

    private static readonly string CategoryControllerPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "Areas",
        "Admin",
        "Controllers",
        "CategoryController.cs");

    private static readonly string CategoryScriptPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "wwwroot",
        "Admin",
        "js",
        "category.js");

    [Fact]
    public void CategoryIndex_should_opt_in_and_expose_reference_filters_and_kpis()
    {
        var view = File.ReadAllText(CategoryIndexPath);

        Assert.Contains("data-gao-ui=\"modern\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Danh sách danh mục", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-category-shell", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"txtSearch\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"ddlStatus\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"ddlPageSize\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("categoryTotalCount", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("categoryActiveCount", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("categoryInactiveCount", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("~/admin/js/category.js", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CategoryResults_should_render_real_hierarchy_metadata_desktop_and_mobile()
    {
        var partial = File.ReadAllText(CategoryTablePath);

        Assert.Contains("Mã danh mục", partial, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Danh mục cha", partial, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Thứ tự", partial, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Ngày tạo", partial, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ParentName", partial, StringComparison.Ordinal);
        Assert.Contains("SortOrder", partial, StringComparison.Ordinal);
        Assert.Contains("CreatedAtUtc", partial, StringComparison.Ordinal);
        Assert.Contains("gds-desktop-list", partial, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gds-mobile-list", partial, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gds-row-card", partial, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(">ID<", partial, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IsRewardEligible", partial, StringComparison.Ordinal);
    }

    [Fact]
    public void CategoryInteraction_should_use_current_routes_and_bounded_async_behavior()
    {
        var view = File.ReadAllText(CategoryIndexPath);
        var script = File.ReadAllText(CategoryScriptPath);

        Assert.Contains("data-search-url", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-toggle-url", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-delete-url", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Swal.fire", script, StringComparison.Ordinal);
        Assert.Contains("Ngừng hoạt động danh mục?", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Kích hoạt lại danh mục?", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Xóa danh mục?", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AbortController", script, StringComparison.Ordinal);
        Assert.Contains("aria-busy", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/Admin/Category/Table", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/Admin/Category/ChangeStatus", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/Admin/Category/Delete\"", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("id=\"deleteModal\"", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CategoryController_should_use_bounded_status_and_summary_contract()
    {
        var controller = File.ReadAllText(CategoryControllerPath);

        Assert.True(
            Regex.IsMatch(controller, @"bool\?\s+status", RegexOptions.IgnoreCase),
            "Category list/search must accept the approved nullable active/inactive filter.");

        Assert.Contains("GetSummaryAsync", controller, StringComparison.Ordinal);

        Assert.True(
            Regex.IsMatch(
                controller,
                @"GetPagedAsync\s*\(\s*searchString\s*,\s*status\s*,",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "Category controller must call the bounded GetPagedAsync overload with search and status.");
    }

    [Fact]
    public void CategoryViews_should_reuse_shared_design_system_without_page_local_css()
    {
        var view = File.ReadAllText(CategoryIndexPath);
        var partial = File.ReadAllText(CategoryTablePath);

        Assert.Contains("gds-kpi-grid", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gds-filter-bar", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gds-list-panel", partial, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gds-action-button", partial, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<style", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<style", partial, StringComparison.OrdinalIgnoreCase);
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
