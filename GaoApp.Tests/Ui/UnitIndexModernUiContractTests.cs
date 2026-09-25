using System.Text.RegularExpressions;
using Xunit;

namespace GaoApp.Tests.Ui;

public sealed class UnitIndexModernUiContractTests
{
    private static readonly string RepositoryRoot =
        FindRepositoryRoot();

    private static readonly string UnitIndexPath =
        Path.Combine(
            RepositoryRoot,
            "GaoApp.Web",
            "Areas",
            "Admin",
            "Views",
            "Unit",
            "Index.cshtml");

    private static readonly string UnitTablePath =
        Path.Combine(
            RepositoryRoot,
            "GaoApp.Web",
            "Areas",
            "Admin",
            "Views",
            "Unit",
            "_UnitTable.cshtml");

    private static readonly string UnitControllerPath =
        Path.Combine(
            RepositoryRoot,
            "GaoApp.Web",
            "Areas",
            "Admin",
            "Controllers",
            "UnitController.cs");

    [Fact]
    public void UnitIndex_should_opt_in_and_expose_reference_filters_and_kpis()
    {
        var view =
            File.ReadAllText(UnitIndexPath);

        Assert.Contains(
            "data-gao-ui=\"modern\"",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "Danh sách đơn vị tính",
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
            "unitTotalCount",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "unitActiveCount",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "unitInactiveCount",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "&status=",
            view,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnitResults_should_render_real_unit_metadata_desktop_and_mobile()
    {
        var partial =
            File.ReadAllText(UnitTablePath);

        Assert.Contains(
            "Mã đơn vị",
            partial,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "Tên đơn vị",
            partial,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "Loại",
            partial,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "Thứ tự",
            partial,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "Ngày tạo",
            partial,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "IsBase",
            partial,
            StringComparison.Ordinal);

        Assert.Contains(
            "SortOrder",
            partial,
            StringComparison.Ordinal);

        Assert.Contains(
            "CreatedAtUtc",
            partial,
            StringComparison.Ordinal);

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

        Assert.DoesNotContain(
            ">ID<",
            partial,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnitVisual_should_use_sweetalert_confirmation_and_no_bootstrap_delete_modal()
    {
        var view =
            File.ReadAllText(UnitIndexPath);

        Assert.Contains(
            "Swal.fire",
            view,
            StringComparison.Ordinal);

        Assert.Contains(
            "showLoaderOnConfirm",
            view,
            StringComparison.Ordinal);

        Assert.Contains(
            "Ngừng hoạt động đơn vị tính?",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "Kích hoạt lại đơn vị tính?",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "Xóa đơn vị tính?",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "id=\"deleteModal\"",
            view,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnitSearch_should_keep_continuous_typing_focus_and_latest_request_protection()
    {
        var view =
            File.ReadAllText(UnitIndexPath);

        Assert.True(
            Regex.IsMatch(
                view,
                "txtSearch\\s*\\.\\s*addEventListener\\s*\\(\\s*['\"]input['\"]",
                RegexOptions.IgnoreCase |
                RegexOptions.CultureInvariant),
            "Unit search must remain input-driven for continuous typing.");

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
    public void UnitController_should_use_bounded_status_and_summary_contract()
    {
        var controller =
            File.ReadAllText(UnitControllerPath);

        Assert.True(
            Regex.IsMatch(
                controller,
                @"bool\?\s+status",
                RegexOptions.IgnoreCase),
            "Unit list/search must accept the approved nullable active/inactive filter.");

        Assert.Contains(
            "GetSummaryAsync",
            controller,
            StringComparison.Ordinal);

        Assert.True(
            Regex.IsMatch(
                controller,
                @"GetPagedAsync\s*\(\s*storeId\s*,\s*search\s*,\s*status\s*,",
                RegexOptions.IgnoreCase |
                RegexOptions.CultureInvariant),
            "Unit controller must call the bounded GetPagedAsync overload with storeId, search and status.");
    }

    [Fact]
    public void UnitViews_should_reuse_shared_design_system_without_page_local_css()
    {
        var view =
            File.ReadAllText(UnitIndexPath);

        var partial =
            File.ReadAllText(UnitTablePath);

        Assert.Contains(
            "gds-kpi-grid",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "gds-filter-bar",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "gds-list-panel",
            partial,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "gds-action-button",
            partial,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "<style",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "<style",
            partial,
            StringComparison.OrdinalIgnoreCase);
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
