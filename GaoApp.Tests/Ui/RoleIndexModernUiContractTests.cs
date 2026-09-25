using Xunit;

namespace GaoApp.Tests.Ui;

public sealed class RoleIndexModernUiContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string IndexPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "Areas", "Admin", "Views", "Roles", "Index.cshtml");

    private static readonly string ScriptPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "wwwroot", "Admin", "js", "role-index.js");

    private static readonly string StylePath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "wwwroot", "Admin", "css", "role-index.css");

    private static readonly string ControllerPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "Areas", "Admin", "Controllers", "RolesController.cs");

    private static readonly string RepositoryPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Infrastructure", "Repositories", "Security", "RoleIndexReadRepository.cs");

    private static readonly string DtoPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Application", "DTOs", "Security", "Roles", "RoleIndexReadDtos.cs");

    private static readonly string ServicePath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Application", "Services", "Security", "RoleIndexReadService.cs");

    [Fact]
    public void Index_should_expose_approved_kpis_filters_table_and_mobile_cards()
    {
        var view = File.ReadAllText(IndexPath);

        Assert.Contains("ViewData[\"container\"] = \"container-fluid\"", view, StringComparison.Ordinal);
        Assert.Contains("data-gao-ui=\"modern\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-role-index", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("row-cols-xl-4", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-role-kpi=\"total\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-role-kpi=\"active\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-role-kpi=\"inactive\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-role-kpi=\"system\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"roleKeyword\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"roleType\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"roleLifecycle\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"rolePageSize\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"roleDesktopList\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"roleMobileList\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"roleMobileFilterSheet\"", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Index_interaction_should_be_realtime_latest_request_safe_and_permission_aware()
    {
        var script = File.ReadAllText(ScriptPath);

        Assert.Contains("AbortController", script, StringComparison.Ordinal);
        Assert.Contains("roleIndexRequestSequence", script, StringComparison.Ordinal);
        Assert.Contains("roleIndexSearchTimer", script, StringComparison.Ordinal);
        Assert.Contains("renderRoleDesktopRows", script, StringComparison.Ordinal);
        Assert.Contains("renderRoleMobileCards", script, StringComparison.Ordinal);
        Assert.Contains("renderRoleCircularPagination", script, StringComparison.Ordinal);
        Assert.Contains("assignedUserCount", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("isSystemRole", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("canPermissions", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("canDelete", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("350", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Module_styles_should_scope_desktop_mobile_filter_sheet_and_safe_area()
    {
        var style = File.ReadAllText(StylePath);

        Assert.Contains(".role-index-page.gds-page", style, StringComparison.Ordinal);
        Assert.Contains(".role-index-desktop-list", style, StringComparison.Ordinal);
        Assert.Contains(".role-index-mobile-list", style, StringComparison.Ordinal);
        Assert.Contains(".role-index-mobile-card", style, StringComparison.Ordinal);
        Assert.Contains(".role-index-filter-sheet", style, StringComparison.Ordinal);
        Assert.Contains("overflow-x: hidden", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("env(safe-area-inset-bottom)", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@media", style, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Controller_should_use_get_only_index_service_and_preserve_existing_security_actions()
    {
        var controller = File.ReadAllText(ControllerPath);

        Assert.Contains("IRoleIndexReadService", controller, StringComparison.Ordinal);
        Assert.Contains("PermissionCodes.Security.Role.View", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"/admin/roles/data\")]", controller, StringComparison.Ordinal);
        Assert.Contains("GetRoleIndexData", controller, StringComparison.Ordinal);
        Assert.Contains("PermissionCodes.Security.Role.Create", controller, StringComparison.Ordinal);
        Assert.Contains("PermissionCodes.Security.Role.Update", controller, StringComparison.Ordinal);
        Assert.Contains("PermissionCodes.Security.Role.Delete", controller, StringComparison.Ordinal);
        Assert.Contains("PermissionCodes.Security.Role.Permissions", controller, StringComparison.Ordinal);
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
        Assert.Contains("IsSystemRole", repository, StringComparison.Ordinal);
        Assert.Contains("ActiveUserCount", repository, StringComparison.Ordinal);
        Assert.Contains("AssignedUserCount", repository, StringComparison.Ordinal);
        Assert.Contains("ApplyLifecycle", repository, StringComparison.Ordinal);
        Assert.Contains("RoleIndexTypes.System", service, StringComparison.Ordinal);
        Assert.Contains("RoleIndexLifecycles.All", service, StringComparison.Ordinal);
        Assert.Contains("RoleIndexSummaryDto", dto, StringComparison.Ordinal);
        Assert.Contains("CanPermissions", dto, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChanges", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ExecuteSql", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AddAsync", repository, StringComparison.OrdinalIgnoreCase);
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
