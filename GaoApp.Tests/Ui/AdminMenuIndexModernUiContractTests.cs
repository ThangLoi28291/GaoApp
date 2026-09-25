using Xunit;

namespace GaoApp.Tests.Ui;

public sealed class AdminMenuIndexModernUiContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string IndexPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "Areas", "Admin", "Views", "AdminMenus", "Index.cshtml");

    private static readonly string ScriptPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "wwwroot", "Admin", "js", "admin-menu-index.js");

    private static readonly string StylePath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "wwwroot", "Admin", "css", "admin-menu-index.css");

    private static readonly string ControllerPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "Areas", "Admin", "Controllers", "AdminMenusController.cs");

    private static readonly string VerticalMenuPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "Areas", "Admin", "Views", "Shared", "Sections", "Menu", "_VerticalMenu.cshtml");

    [Fact]
    public void Index_should_expose_approved_kpis_filters_tree_table_quick_view_and_mobile_cards()
    {
        var view = File.ReadAllText(IndexPath);

        Assert.Contains("ViewData[\"container\"] = \"container-fluid\"", view, StringComparison.Ordinal);
        Assert.Contains("data-gao-ui=\"modern\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-admin-menu-index", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("row-cols-xl-4", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-admin-menu-kpi=\"total\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-admin-menu-kpi=\"active\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-admin-menu-kpi=\"inactive\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-admin-menu-kpi=\"custom\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"adminMenuKeyword\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"adminMenuLevel\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"adminMenuType\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"adminMenuLifecycle\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"adminMenuPageSize\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"adminMenuDesktopList\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"adminMenuMobileList\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"adminMenuQuickView\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"adminMenuMobileFilterSheet\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"adminMenuDeleteConfirmation\"", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Index_should_preserve_existing_create_edit_delete_routes_and_antiforgery_posts()
    {
        var view = File.ReadAllText(IndexPath);

        Assert.Contains("asp-action=\"Create\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("asp-action=\"Edit\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("asp-action=\"Delete\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("method=\"post\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@Html.AntiForgeryToken()", view, StringComparison.Ordinal);
        Assert.Contains("data-admin-menu-delete-form", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-delete-locked", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Interaction_should_be_client_side_realtime_no_diacritic_and_presentation_only()
    {
        var script = File.ReadAllText(ScriptPath);

        Assert.Contains("adminMenuSearchTimer", script, StringComparison.Ordinal);
        Assert.Contains("normalizeSearchText", script, StringComparison.Ordinal);
        Assert.Contains("normalize('NFD')", script, StringComparison.Ordinal);
        Assert.Contains("350", script, StringComparison.Ordinal);
        Assert.Contains("applyAdminMenuFilters", script, StringComparison.Ordinal);
        Assert.Contains("renderAdminMenuSummary", script, StringComparison.Ordinal);
        Assert.Contains("renderAdminMenuCircularPagination", script, StringComparison.Ordinal);
        Assert.Contains("openAdminMenuQuickView", script, StringComparison.Ordinal);
        Assert.Contains("showDeleteConfirmation", script, StringComparison.Ordinal);
        Assert.Contains("data-admin-menu-collapse", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fetch(", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("XMLHttpRequest", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dragstart", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Module_styles_should_scope_tree_table_mobile_sheets_actions_and_safe_area()
    {
        var style = File.ReadAllText(StylePath);

        Assert.Contains(".admin-menu-index-page.gds-page", style, StringComparison.Ordinal);
        Assert.Contains(".admin-menu-index-desktop-list", style, StringComparison.Ordinal);
        Assert.Contains(".admin-menu-index-mobile-list", style, StringComparison.Ordinal);
        Assert.Contains(".admin-menu-index-mobile-card", style, StringComparison.Ordinal);
        Assert.Contains(".admin-menu-index-filter-sheet", style, StringComparison.Ordinal);
        Assert.Contains(".admin-menu-index-quick-sheet", style, StringComparison.Ordinal);
        Assert.Contains(".admin-menu-index-action", style, StringComparison.Ordinal);
        Assert.Contains("overflow-x: hidden", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("overflow-wrap", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("env(safe-area-inset-bottom)", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@media", style, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Locked_controller_and_dynamic_sidebar_should_retain_authority_boundaries()
    {
        var controller = File.ReadAllText(ControllerPath);
        var verticalMenu = File.ReadAllText(VerticalMenuPath);

        Assert.Contains("PermissionCodes.Security.Role.Permissions", controller, StringComparison.Ordinal);
        Assert.Contains("GetForAdminAsync(CurrentStoreId", controller, StringComparison.Ordinal);
        Assert.Contains("[ValidateAntiForgeryToken]", controller, StringComparison.Ordinal);
        Assert.Contains("DeleteAsync(CurrentStoreId", controller, StringComparison.Ordinal);
        Assert.Contains("GetForRenderAsync", verticalMenu, StringComparison.Ordinal);
        Assert.Contains("Where(x => x.IsActive)", verticalMenu, StringComparison.Ordinal);
        Assert.Contains("children.Any()", verticalMenu, StringComparison.Ordinal);
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
