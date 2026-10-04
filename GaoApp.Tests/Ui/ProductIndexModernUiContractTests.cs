using System.Text.RegularExpressions;
using Xunit;

namespace GaoApp.Tests.Ui;

public sealed class ProductIndexModernUiContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string IndexPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "Areas",
        "Admin",
        "Views",
        "Product",
        "Index.cshtml");

    private static readonly string TablePath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "Areas",
        "Admin",
        "Views",
        "Product",
        "_ProductTable.cshtml");

    private static readonly string ControllerPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "Areas",
        "Admin",
        "Controllers",
        "ProductController.cs");

    private static readonly string ScriptPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "wwwroot",
        "Admin",
        "js",
        "products.index.js");

    [Fact]
    public void Index_should_opt_in_and_expose_approved_product_filters_and_kpis()
    {
        var view = File.ReadAllText(IndexPath);

        Assert.Contains("data-gao-ui=\"modern\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-product-shell", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"txtSearch\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"ddlCategory\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"ddlLifecycle\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"ddlPageSize\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("productTotalCount", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("productPosAllowedCount", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("productNotForPosCount", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("productInactiveCount", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("~/admin/js/products.index.js", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<style", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Results_should_render_approved_desktop_and_mobile_hierarchy_without_internal_id()
    {
        var partial = File.ReadAllText(TablePath);

        Assert.Contains("gds-desktop-list", partial, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gds-mobile-list", partial, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gds-row-card", partial, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PrimaryImageUrl", partial, StringComparison.Ordinal);
        Assert.Contains("BrandName", partial, StringComparison.Ordinal);
        Assert.Contains("CategoryName", partial, StringComparison.Ordinal);
        Assert.Contains("BaseUnitName", partial, StringComparison.Ordinal);
        Assert.Contains("BasePrice", partial, StringComparison.Ordinal);
        Assert.Contains("VariantCount", partial, StringComparison.Ordinal);
        Assert.DoesNotContain("ID sản phẩm", partial, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(">Mã<", partial, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<style", partial, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Interaction_should_use_current_routes_url_state_and_latest_request_protection()
    {
        Assert.True(File.Exists(ScriptPath), $"Missing Product list script: {ScriptPath}");

        var view = File.ReadAllText(IndexPath);
        var script = File.ReadAllText(ScriptPath);

        Assert.Contains("data-search-url", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-toggle-url", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-delete-url", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Swal.fire", script, StringComparison.Ordinal);
        Assert.Contains("AbortController", script, StringComparison.Ordinal);
        Assert.Contains("requestSequence", script, StringComparison.Ordinal);
        Assert.Contains("history.pushState", script, StringComparison.Ordinal);
        Assert.Contains("history.replaceState", script, StringComparison.Ordinal);
        Assert.Contains("popstate", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aria-busy", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("txtSearch.focus", script, StringComparison.Ordinal);
        Assert.Contains("RequestVerificationToken", script, StringComparison.Ordinal);
        Assert.DoesNotContain("if (isLoading) return", script, StringComparison.Ordinal);
        Assert.DoesNotContain("window.confirm", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Controller_should_use_bounded_category_lifecycle_and_summary_contract()
    {
        var controller = File.ReadAllText(ControllerPath);

        Assert.True(
            Regex.IsMatch(controller, @"int\?\s+categoryId", RegexOptions.IgnoreCase),
            "Product list/search must accept nullable CategoryId.");

        Assert.True(
            Regex.IsMatch(controller, @"string\?\s+lifecycle", RegexOptions.IgnoreCase),
            "Product list/search must accept the approved lifecycle filter.");

        Assert.Contains("GetSummaryAsync", controller, StringComparison.Ordinal);

        Assert.True(
            Regex.IsMatch(
                controller,
                @"SearchCatalogAsync\s*\(\s*storeId\s*,\s*search\s*,\s*categoryId\s*,\s*isActive\s*,\s*isSellable\s*,",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "Product controller must call the bounded filtered read overload.");
    }

    [Fact]
    public void Product_write_and_workspace_views_should_remain_outside_modern_opt_in()
    {
        var productViews = new[]
        {
            "Create.cshtml",
            "Edit.cshtml",
            "Detail.cshtml",
            "_Variants.cshtml",
            "_VariantUnitConversionsModal.cshtml"
        };

        foreach (var viewName in productViews)
        {
            var path = Path.Combine(
                RepositoryRoot,
                "GaoApp.Web",
                "Areas",
                "Admin",
                "Views",
                "Product",
                viewName);

            Assert.DoesNotContain(
                "data-gao-ui=\"modern\"",
                File.ReadAllText(path),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Practical_remediation_should_strengthen_hierarchy_and_offer_quick_view_and_image_zoom()
    {
        var index = File.ReadAllText(IndexPath);
        var partial = File.ReadAllText(TablePath);
        var script = File.ReadAllText(ScriptPath);

        Assert.Contains("ViewData[\"container\"] = \"container-fluid\"", index, StringComparison.Ordinal);
        Assert.Contains("data-product-page", index, StringComparison.Ordinal);
        Assert.Contains("row-cols-xl-4", index, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("card shadow-sm", index, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("productQuickViewModal", index, StringComparison.Ordinal);
        Assert.Contains("productQuickViewImage", index, StringComparison.Ordinal);

        Assert.Contains("Đơn giá", partial, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Giá cơ bản", partial, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("js-product-image", partial, StringComparison.Ordinal);
        Assert.Contains("js-quick-view", partial, StringComparison.Ordinal);
        Assert.Contains("data-product-row", partial, StringComparison.Ordinal);
        Assert.Contains("data-quick-supplier", partial, StringComparison.Ordinal);
        Assert.Contains("data-quick-tax", partial, StringComparison.Ordinal);

        Assert.Contains("dblclick", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("showQuickView", script, StringComparison.Ordinal);
        Assert.Contains("productQuickViewImage", script, StringComparison.Ordinal);
        Assert.Contains(".js-product-image", script, StringComparison.Ordinal);
        Assert.Contains("productImageHoverPreview", script, StringComparison.Ordinal);
        Assert.Contains("pointerenter", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pointerleave", script, StringComparison.OrdinalIgnoreCase);
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
