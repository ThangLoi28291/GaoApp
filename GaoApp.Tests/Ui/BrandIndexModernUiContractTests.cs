using System.Text.RegularExpressions;
using Xunit;

namespace GaoApp.Tests.Ui;

public sealed class BrandIndexModernUiContractTests
{
    private static readonly string RepositoryRoot =
        FindRepositoryRoot();

    private static readonly string BrandIndexPath =
        Path.Combine(
            RepositoryRoot,
            "GaoApp.Web",
            "Areas",
            "Admin",
            "Views",
            "Brand",
            "Index.cshtml");

    private static readonly string BrandTablePath =
        Path.Combine(
            RepositoryRoot,
            "GaoApp.Web",
            "Areas",
            "Admin",
            "Views",
            "Brand",
            "_BrandTable.cshtml");

    private static readonly string BrandControllerPath =
        Path.Combine(
            RepositoryRoot,
            "GaoApp.Web",
            "Areas",
            "Admin",
            "Controllers",
            "BrandController.cs");

    private static readonly string DesignSystemCssPath =
        Path.Combine(
            RepositoryRoot,
            "GaoApp.Web",
            "wwwroot",
            "Admin",
            "css",
            "gaoapp.design-system.css");

    [Fact]
    public void BrandIndex_should_opt_in_and_expose_approved_real_filters()
    {
        var view =
            File.ReadAllText(BrandIndexPath);

        Assert.Contains(
            "data-gao-ui=\"modern\"",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "id=\"txtSearch\"",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "id=\"ddlStatus\"",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "id=\"ddlPageSize\"",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "brandTotalCount",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "brandActiveCount",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "brandInactiveCount",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "&status=",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "Thêm thương hiệu",
            view,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BrandVisualV2_should_keep_header_compact_and_remove_redundant_copy()
    {
        var view =
            File.ReadAllText(BrandIndexPath);

        var partial =
            File.ReadAllText(BrandTablePath);

        Assert.Contains(
            "gds-page-header--compact",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            ">Danh sách thương hiệu<",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "Danh mục sản phẩm",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "Quản lý thương hiệu dùng trong danh mục",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "gds-list-panel__title\">Thương hiệu",
            partial,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "gds-list-footer",
            partial,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BrandResults_should_render_created_date_desktop_table_and_mobile_cards()
    {
        var partial =
            File.ReadAllText(BrandTablePath);

        Assert.Contains(
            "Mã thương hiệu",
            partial,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "Tên thương hiệu",
            partial,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "Ngày tạo",
            partial,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "CreatedAtUtc",
            partial,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "gds-desktop-list",
            partial,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "gds-mobile-list",
            partial,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "gds-row-card",
            partial,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "js-toggle-status",
            partial,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "js-delete",
            partial,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "data-status=",
            partial,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "data-name=",
            partial,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            ">ID<",
            partial,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BrandVisualV2_should_use_sweetalert_confirmations_and_bounded_loading_states()
    {
        var view =
            File.ReadAllText(BrandIndexPath);

        Assert.Contains(
            "Swal.fire",
            view,
            StringComparison.Ordinal);

        Assert.Contains(
            "showLoaderOnConfirm",
            view,
            StringComparison.Ordinal);

        Assert.Contains(
            "preConfirm",
            view,
            StringComparison.Ordinal);

        Assert.Contains(
            "Ngừng hoạt động thương hiệu?",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "Kích hoạt lại thương hiệu?",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "Xóa thương hiệu?",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "gds-results-region",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "aria-busy",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "id=\"deleteModal\"",
            view,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BrandSearch_should_keep_continuous_typing_focus_and_latest_request_protection()
    {
        var view =
            File.ReadAllText(BrandIndexPath);

        Assert.True(
            Regex.IsMatch(
                view,
                "txtSearch\\s*\\.\\s*addEventListener\\s*\\(\\s*['\"]input['\"]",
                RegexOptions.IgnoreCase |
                RegexOptions.CultureInvariant),
            "Brand search must remain input-driven for continuous typing.");

        Assert.Contains(
            "new AbortController()",
            view,
            StringComparison.Ordinal);

        Assert.Contains(
            "searchRequestSequence",
            view,
            StringComparison.Ordinal);

        Assert.Contains(
            "setResultsLoading(true)",
            view,
            StringComparison.Ordinal);

        Assert.Contains(
            "aria-busy",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "txtSearch.focus()",
            view,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "txtSearch.disabled",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "txtSearch.setAttribute('disabled'",
            view,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BrandController_should_use_bounded_status_and_summary_contract()
    {
        var controller =
            File.ReadAllText(BrandControllerPath);

        Assert.True(
            Regex.IsMatch(
                controller,
                @"bool\?\s+status",
                RegexOptions.IgnoreCase),
            "Brand list/search must accept the approved nullable active/inactive filter.");

        Assert.Contains(
            "GetSummaryAsync",
            controller,
            StringComparison.Ordinal);

        Assert.Contains(
            "GetPagedAsync(storeId, search, status",
            controller,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DesignSystem_should_expose_reusable_crud_list_primitives_without_brand_specific_css()
    {
        var css =
            File.ReadAllText(DesignSystemCssPath);

        Assert.Contains(
            ".gds-kpi-grid",
            css,
            StringComparison.Ordinal);

        Assert.Contains(
            ".gds-kpi-card",
            css,
            StringComparison.Ordinal);

        Assert.Contains(
            ".gds-list-panel",
            css,
            StringComparison.Ordinal);

        Assert.Contains(
            ".gds-list-footer",
            css,
            StringComparison.Ordinal);

        Assert.Contains(
            ".gds-mobile-list",
            css,
            StringComparison.Ordinal);

        Assert.Contains(
            ".gds-row-card",
            css,
            StringComparison.Ordinal);

        Assert.Contains(
            ".gds-action-button",
            css,
            StringComparison.Ordinal);

        Assert.Contains(
            ".gds-swal-popup",
            css,
            StringComparison.Ordinal);

        Assert.Contains(
            "translateY(-1px)",
            css,
            StringComparison.Ordinal);

        Assert.Contains(
            "@media (max-width: 767.98px)",
            css,
            StringComparison.OrdinalIgnoreCase);

        Assert.False(
            Regex.IsMatch(
                css,
                @"\.brand-[A-Za-z0-9_-]+",
                RegexOptions.IgnoreCase),
            "Shared design-system CSS must remain reusable instead of embedding Brand-only selectors.");
    }

    private static string FindRepositoryRoot()
    {
        var directory =
            new DirectoryInfo(
                AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (
                File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "GaoApp.sln"))
            )
            {
                return directory.FullName;
            }

            directory =
                directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate GaoApp repository root from test output directory.");
    }
}
