using System.Text.RegularExpressions;
using Xunit;

namespace GaoApp.Tests.Ui;

public sealed class ProductAttributeIndexModernUiContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string IndexPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "Areas",
        "Admin",
        "Views",
        "ProductAttribute",
        "Index.cshtml");

    private static readonly string TablePath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "Areas",
        "Admin",
        "Views",
        "ProductAttribute",
        "_ProductAttributeTable.cshtml");

    private static readonly string ControllerPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "Areas",
        "Admin",
        "Controllers",
        "ProductAttributeController.cs");

    private static readonly string ScriptPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "wwwroot",
        "Admin",
        "js",
        "product-attributes.index.js");

    [Fact]
    public void Index_should_opt_in_and_expose_truthful_reference_filters_and_kpis()
    {
        var view = File.ReadAllText(IndexPath);

        Assert.Contains("data-gao-ui=\"modern\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-product-attribute-shell", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"txtSearch\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"ddlStatus\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"ddlPageSize\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("productAttributeTotalCount", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("productAttributeActiveCount", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("productAttributeInactiveCount", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("~/admin/js/product-attributes.index.js", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("~/admin/css/product-attributes.css", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Results_should_render_business_metadata_desktop_and_mobile_without_internal_id()
    {
        var partial = File.ReadAllText(TablePath);

        Assert.Contains("Mã thuộc tính", partial, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Tên thuộc tính", partial, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Ngày tạo", partial, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CreatedAtUtc", partial, StringComparison.Ordinal);
        Assert.Contains("AttributeValue", partial, StringComparison.Ordinal);
        Assert.Contains("gds-desktop-list", partial, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gds-mobile-list", partial, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gds-row-card", partial, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(">ID<", partial, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Interaction_should_use_current_routes_and_latest_request_protection()
    {
        var view = File.ReadAllText(IndexPath);
        var script = File.ReadAllText(ScriptPath);

        Assert.Contains("data-search-url", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-toggle-url", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-delete-url", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Swal.fire", script, StringComparison.Ordinal);
        Assert.Contains("AbortController", script, StringComparison.Ordinal);
        Assert.Contains("requestSequence", script, StringComparison.Ordinal);
        Assert.Contains("aria-busy", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("txtSearch.focus", script, StringComparison.Ordinal);
        Assert.DoesNotContain("if (isLoading) return", script, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"deleteModal\"", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Controller_should_use_bounded_status_and_summary_contract()
    {
        var controller = File.ReadAllText(ControllerPath);

        Assert.True(
            Regex.IsMatch(controller, @"bool\?\s+status", RegexOptions.IgnoreCase),
            "ProductAttribute list/search must accept nullable status.");

        Assert.Contains("GetSummaryAsync", controller, StringComparison.Ordinal);

        Assert.True(
            Regex.IsMatch(
                controller,
                @"GetPagedAsync\s*\(\s*storeId\s*,\s*search\s*,\s*status\s*,",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "ProductAttribute controller must pass search and status to paging.");
    }

    [Fact]
    public void Views_should_reuse_shared_design_system_without_page_local_css()
    {
        var view = File.ReadAllText(IndexPath);
        var partial = File.ReadAllText(TablePath);

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
