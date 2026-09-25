using Xunit;

namespace GaoApp.Tests.Ui;

public sealed class EmployeeIndexModernUiContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string IndexPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "Areas", "Admin", "Views", "UserInStores", "Index.cshtml");

    private static readonly string ScriptPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "wwwroot", "Admin", "js", "employee-index.js");

    private static readonly string StylePath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "wwwroot", "Admin", "css", "employee-index.css");

    private static readonly string ControllerPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "Areas", "Admin", "Controllers", "UserInStoresController.cs");

    private static readonly string RepositoryPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Infrastructure", "Repositories", "Security", "EmployeeIndexReadRepository.cs");

    private static readonly string DtoPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Application", "DTOs", "Security", "UserInStores", "EmployeeIndexReadDtos.cs");

    private static readonly string ServicePath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Application", "Services", "Security", "EmployeeIndexReadService.cs");

    [Fact]
    public void Index_should_expose_approved_kpis_filters_table_quick_view_and_mobile_cards()
    {
        var view = File.ReadAllText(IndexPath);

        Assert.Contains("ViewData[\"container\"] = \"container-fluid\"", view, StringComparison.Ordinal);
        Assert.Contains("data-gao-ui=\"modern\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-employee-index", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("row-cols-xl-4", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-employee-kpi=\"total\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-employee-kpi=\"active\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-employee-kpi=\"inactive\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-employee-kpi=\"roles\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"employeeKeyword\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"employeeRole\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"employeeLifecycle\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"employeePageSize\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"employeeDesktopList\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"employeeMobileList\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"employeeQuickView\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"employeeMobileFilterSheet\"", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Index_interaction_should_be_realtime_latest_request_safe_and_permission_aware()
    {
        var script = File.ReadAllText(ScriptPath);

        Assert.Contains("AbortController", script, StringComparison.Ordinal);
        Assert.Contains("employeeIndexRequestSequence", script, StringComparison.Ordinal);
        Assert.Contains("employeeIndexSearchTimer", script, StringComparison.Ordinal);
        Assert.Contains("renderEmployeeDesktopRows", script, StringComparison.Ordinal);
        Assert.Contains("renderEmployeeMobileCards", script, StringComparison.Ordinal);
        Assert.Contains("renderEmployeeCircularPagination", script, StringComparison.Ordinal);
        Assert.Contains("openEmployeeQuickView", script, StringComparison.Ordinal);
        Assert.Contains("employee-index-status-action", script, StringComparison.Ordinal);
        Assert.Contains("renderStatus(item, true, responsePermissions)", script, StringComparison.Ordinal);
        Assert.Contains("canUpdate", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("canToggleActive", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("350", script, StringComparison.Ordinal);
        Assert.DoesNotContain("delete", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Module_styles_should_scope_desktop_mobile_sheets_and_safe_area()
    {
        var style = File.ReadAllText(StylePath);

        Assert.Contains(".employee-index-page.gds-page", style, StringComparison.Ordinal);
        Assert.Contains(".employee-index-desktop-list", style, StringComparison.Ordinal);
        Assert.Contains(".employee-index-mobile-list", style, StringComparison.Ordinal);
        Assert.Contains(".employee-index-mobile-card", style, StringComparison.Ordinal);
        Assert.Contains(".employee-index-filter-sheet", style, StringComparison.Ordinal);
        Assert.Contains(".employee-index-quick-sheet", style, StringComparison.Ordinal);
        Assert.Contains(".employee-index-action i", style, StringComparison.Ordinal);
        Assert.Contains("font-size: 1.15rem", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("overflow-x: hidden", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("overflow-wrap", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("env(safe-area-inset-bottom)", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@media", style, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Controller_should_use_get_only_index_service_and_preserve_security_actions()
    {
        var controller = File.ReadAllText(ControllerPath);

        Assert.Contains("IEmployeeIndexReadService", controller, StringComparison.Ordinal);
        Assert.Contains("PermissionCodes.Security.UserInStore.View", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"/admin/employees/data\")]", controller, StringComparison.Ordinal);
        Assert.Contains("GetEmployeeIndexData", controller, StringComparison.Ordinal);
        Assert.Contains("PermissionCodes.Security.UserInStore.Create", controller, StringComparison.Ordinal);
        Assert.Contains("PermissionCodes.Security.UserInStore.Update", controller, StringComparison.Ordinal);
        Assert.Contains("PermissionCodes.Security.UserInStore.Delete", controller, StringComparison.Ordinal);
        Assert.Contains("ResetPassword", controller, StringComparison.Ordinal);
        Assert.Contains("ToggleActive", controller, StringComparison.Ordinal);
        Assert.Contains("[ValidateAntiForgeryToken]", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_stack_should_apply_exact_scope_counts_search_and_no_write_behavior()
    {
        var repository = File.ReadAllText(RepositoryPath);
        var dto = File.ReadAllText(DtoPath);
        var service = File.ReadAllText(ServicePath);

        Assert.Contains("Latin1_General_100_CI_AI", repository, StringComparison.Ordinal);
        Assert.Contains("AsNoTracking", repository, StringComparison.Ordinal);
        Assert.Contains("StoreId == storeId", repository, StringComparison.Ordinal);
        Assert.Contains("DistinctRoleCount", repository, StringComparison.Ordinal);
        Assert.Contains("ApplyLifecycle", repository, StringComparison.Ordinal);
        Assert.Contains("PhoneNumber", repository, StringComparison.Ordinal);
        Assert.Contains("PositionName", repository, StringComparison.Ordinal);
        Assert.Contains("EmployeeIndexLifecycles.All", service, StringComparison.Ordinal);
        Assert.Contains("EmployeeIndexSummaryDto", dto, StringComparison.Ordinal);
        Assert.Contains("CanToggleActive", dto, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChanges", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ExecuteSql", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AddAsync", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PasswordHash", repository, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var configured = Environment.GetEnvironmentVariable("GAOAPP_REPOSITORY_ROOT");
        if (!string.IsNullOrWhiteSpace(configured)
            && File.Exists(Path.Combine(configured, "GaoApp.sln")))
        {
            return configured;
        }

        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
                    return directory.FullName;

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException(
            "Could not locate GaoApp repository root from test output directory.");
    }
}
