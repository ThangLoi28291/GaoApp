using System.Text.RegularExpressions;
using Xunit;

namespace GaoApp.Tests.Ui;

public sealed class SupplierIndexModernUiContractTests
{
    private static readonly string RepositoryRoot =
        FindRepositoryRoot();

    private static readonly string SupplierIndexPath =
        Path.Combine(
            RepositoryRoot,
            "GaoApp.Web",
            "Areas",
            "Admin",
            "Views",
            "Supplier",
            "Index.cshtml");

    private static readonly string SupplierTablePath =
        Path.Combine(
            RepositoryRoot,
            "GaoApp.Web",
            "Areas",
            "Admin",
            "Views",
            "Supplier",
            "_SupplierTable.cshtml");

    private static readonly string SupplierScriptPath =
        Path.Combine(
            RepositoryRoot,
            "GaoApp.Web",
            "wwwroot",
            "Admin",
            "js",
            "suppliers.index.js");

    private static readonly string SupplierControllerPath =
        Path.Combine(
            RepositoryRoot,
            "GaoApp.Web",
            "Areas",
            "Admin",
            "Controllers",
            "SupplierController.cs");

    private static readonly string DesignSystemCssPath =
        Path.Combine(
            RepositoryRoot,
            "GaoApp.Web",
            "wwwroot",
            "Admin",
            "css",
            "gaoapp.design-system.css");

    [Fact]
    public void SupplierIndex_should_opt_in_and_expose_only_approved_list_filters()
    {
        var view =
            File.ReadAllText(SupplierIndexPath);

        Assert.Contains(
            "data-gao-ui=\"modern\"",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "data-supplier-shell",
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
            "supplierTotalCount",
            view,
            StringComparison.Ordinal);

        Assert.Contains(
            "supplierActiveCount",
            view,
            StringComparison.Ordinal);

        Assert.Contains(
            "supplierInactiveCount",
            view,
            StringComparison.Ordinal);

        Assert.Contains(
            "suppliers.index.js",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "số điện thoại hoặc MST",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "suppliers.css",
            view,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SupplierResults_should_render_approved_business_fields_on_desktop_and_mobile()
    {
        var partial =
            File.ReadAllText(SupplierTablePath);

        Assert.Contains(
            "Mã nhà cung cấp",
            partial,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "Nhà cung cấp",
            partial,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "Liên hệ",
            partial,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "Mã số thuế",
            partial,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "Ngày tạo",
            partial,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "ContactName",
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
            "js-toggle-status",
            partial,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "js-delete",
            partial,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            ">ID<",
            partial,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "Address",
            partial,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "Note",
            partial,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SupplierInteractions_should_use_external_script_sweetalert_and_bounded_loading()
    {
        var view =
            File.ReadAllText(SupplierIndexPath);

        var script =
            File.ReadAllText(SupplierScriptPath);

        Assert.Contains(
            "Swal.fire",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "showLoaderOnConfirm",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "preConfirm",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "GaoAppNotify",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "status: ddlStatus.value",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "AbortController",
            script,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "txtSearch.disabled",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "aria-busy",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "aria-busy",
            script,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "id=\"deleteModal\"",
            view,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "deleteModal",
            script,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SupplierController_should_use_nullable_status_and_unfiltered_summary()
    {
        var controller =
            File.ReadAllText(SupplierControllerPath);

        Assert.True(
            Regex.Matches(
                controller,
                @"bool\?\s+status",
                RegexOptions.IgnoreCase).Count >= 2,
            "Supplier Index and Search must both accept the approved nullable status filter.");

        Assert.Contains(
            "GetSummaryAsync",
            controller,
            StringComparison.Ordinal);

        Assert.True(
            Regex.IsMatch(
                controller,
                @"GetPagedAsync\s*\(\s*storeId\s*,\s*search\s*,\s*status\s*,",
                RegexOptions.IgnoreCase),
            "Supplier list/search must pass the nullable status filter to the bounded query overload.");
    }

    [Fact]
    public void Shared_design_system_should_supply_reusable_primitives_without_supplier_css()
    {
        var css =
            File.ReadAllText(DesignSystemCssPath);

        Assert.Contains(
            ".gds-kpi-grid",
            css,
            StringComparison.Ordinal);

        Assert.Contains(
            ".gds-list-panel",
            css,
            StringComparison.Ordinal);

        Assert.Contains(
            ".gds-mobile-list",
            css,
            StringComparison.Ordinal);

        Assert.Contains(
            ".gds-swal-popup",
            css,
            StringComparison.Ordinal);

        Assert.False(
            Regex.IsMatch(
                css,
                @"\.supplier-[A-Za-z0-9_-]+",
                RegexOptions.IgnoreCase),
            "Shared design-system CSS must not embed Supplier-only selectors.");
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
