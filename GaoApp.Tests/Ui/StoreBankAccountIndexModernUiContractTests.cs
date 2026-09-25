using Xunit;

namespace GaoApp.Tests.Ui;

public sealed class StoreBankAccountIndexModernUiContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string IndexPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "Areas", "Admin", "Views", "StoreBankAccounts", "Index.cshtml");

    private static readonly string ScriptPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "wwwroot", "Admin", "js", "store-bank-account-index.js");

    private static readonly string StylePath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "wwwroot", "Admin", "css", "store-bank-account-index.css");

    private static readonly string ControllerPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "Areas", "Admin", "Controllers", "StoreBankAccountsController.cs");

    private static readonly string RepositoryPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Infrastructure", "Repositories", "StoreBankAccounts", "StoreBankAccountIndexReadRepository.cs");

    private static readonly string DtoPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Application", "DTOs", "StoreBankAccounts", "StoreBankAccountIndexReadDtos.cs");

    private static readonly string ServicePath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Application", "Services", "StoreBankAccounts", "StoreBankAccountIndexReadService.cs");

    [Fact]
    public void Index_should_expose_approved_kpis_filters_table_quick_view_and_mobile_cards()
    {
        var view = File.ReadAllText(IndexPath);

        Assert.Contains("ViewData[\"container\"] = \"container-fluid\"", view, StringComparison.Ordinal);
        Assert.Contains("data-gao-ui=\"modern\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-store-bank-account-index", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("row-cols-xl-4", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-bank-kpi=\"total\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-bank-kpi=\"active\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-bank-kpi=\"inactive\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-bank-kpi=\"default\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"storeBankKeyword\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"storeBankQrMode\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"storeBankConfirmMode\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"storeBankLifecycle\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"storeBankDefaultRole\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"storeBankPageSize\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"storeBankDesktopList\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"storeBankMobileList\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"storeBankQuickView\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"storeBankMobileFilterSheet\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"storeBankConfirmation\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@Html.AntiForgeryToken()", view, StringComparison.Ordinal);
        Assert.DoesNotContain(">ID<", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Interaction_should_be_realtime_latest_request_safe_and_preserve_existing_writes()
    {
        var script = File.ReadAllText(ScriptPath);

        Assert.Contains("AbortController", script, StringComparison.Ordinal);
        Assert.Contains("storeBankAccountIndexRequestSequence", script, StringComparison.Ordinal);
        Assert.Contains("storeBankAccountSearchTimer", script, StringComparison.Ordinal);
        Assert.Contains("350", script, StringComparison.Ordinal);
        Assert.Contains("renderStoreBankDesktopRows", script, StringComparison.Ordinal);
        Assert.Contains("renderStoreBankMobileCards", script, StringComparison.Ordinal);
        Assert.Contains("renderStoreBankCircularPagination", script, StringComparison.Ordinal);
        Assert.Contains("openStoreBankQuickView", script, StringComparison.Ordinal);
        Assert.Contains("showStoreBankConfirmation", script, StringComparison.Ordinal);
        Assert.Contains("store-bank-status-action", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("store-bank-default-action", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("__RequestVerificationToken", script, StringComparison.Ordinal);
        Assert.Contains("ToggleStatus", script, StringComparison.Ordinal);
        Assert.Contains("SetDefault", script, StringComparison.Ordinal);
        Assert.DoesNotContain("ApiClientId", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ApiSecret", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CallbackSecret", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("QRCode", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Module_styles_should_scope_desktop_mobile_sheets_actions_and_safe_area()
    {
        var style = File.ReadAllText(StylePath);

        Assert.Contains(".store-bank-account-index-page.gds-page", style, StringComparison.Ordinal);
        Assert.Contains(".store-bank-account-desktop-list", style, StringComparison.Ordinal);
        Assert.Contains(".store-bank-account-mobile-list", style, StringComparison.Ordinal);
        Assert.Contains(".store-bank-account-mobile-card", style, StringComparison.Ordinal);
        Assert.Contains(".store-bank-account-filter-sheet", style, StringComparison.Ordinal);
        Assert.Contains(".store-bank-account-quick-sheet", style, StringComparison.Ordinal);
        Assert.Contains(".store-bank-account-confirmation-sheet", style, StringComparison.Ordinal);
        Assert.Contains(".store-bank-account-action", style, StringComparison.Ordinal);
        Assert.Contains("overflow-x: hidden", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("overflow-wrap", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("env(safe-area-inset-bottom)", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@media", style, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Controller_should_add_get_only_store_scoped_data_and_preserve_legacy_actions()
    {
        var controller = File.ReadAllText(ControllerPath);

        Assert.Contains("BaseAdminController", controller, StringComparison.Ordinal);
        Assert.Contains("IStoreBankAccountService", controller, StringComparison.Ordinal);
        Assert.Contains("IStoreBankAccountIndexReadService", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"/admin/store-bank-accounts/data\")]", controller, StringComparison.Ordinal);
        Assert.Contains("GetStoreBankAccountIndexData", controller, StringComparison.Ordinal);
        Assert.Contains("CurrentStoreId", controller, StringComparison.Ordinal);
        Assert.Contains("Task<IActionResult> Search", controller, StringComparison.Ordinal);
        Assert.Contains("Task<IActionResult> Edit", controller, StringComparison.Ordinal);
        Assert.Contains("Task<IActionResult> ToggleStatus", controller, StringComparison.Ordinal);
        Assert.Contains("Task<IActionResult> SetDefault", controller, StringComparison.Ordinal);
        Assert.True(
            controller.Split("[ValidateAntiForgeryToken]", StringSplitOptions.None).Length - 1 >= 3,
            "Existing Edit, ToggleStatus and SetDefault POST actions must keep antiforgery validation.");
    }

    [Fact]
    public void Read_stack_should_apply_exact_scope_summary_search_filters_and_no_write_or_secret_behavior()
    {
        var repository = File.ReadAllText(RepositoryPath);
        var dto = File.ReadAllText(DtoPath);
        var service = File.ReadAllText(ServicePath);

        Assert.Contains("Latin1_General_100_CI_AI", repository, StringComparison.Ordinal);
        Assert.Contains("AsNoTracking", repository, StringComparison.Ordinal);
        Assert.Contains("StoreId == storeId", repository, StringComparison.Ordinal);
        Assert.Contains("TotalAccounts", repository, StringComparison.Ordinal);
        Assert.Contains("ActiveAccounts", repository, StringComparison.Ordinal);
        Assert.Contains("InactiveAccounts", repository, StringComparison.Ordinal);
        Assert.Contains("DefaultAccounts", repository, StringComparison.Ordinal);
        Assert.Contains("ApplyQrMode", repository, StringComparison.Ordinal);
        Assert.Contains("ApplyConfirmMode", repository, StringComparison.Ordinal);
        Assert.Contains("ApplyLifecycle", repository, StringComparison.Ordinal);
        Assert.Contains("ApplyDefaultRole", repository, StringComparison.Ordinal);
        Assert.Contains("AccountNumber", repository, StringComparison.Ordinal);
        Assert.Contains("AccountName", repository, StringComparison.Ordinal);
        Assert.Contains("ProviderCode", repository, StringComparison.Ordinal);
        Assert.Contains("StoreBankAccountIndexSummaryDto", dto, StringComparison.Ordinal);
        Assert.Contains("StoreBankAccountIndexDefaults.All", service, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChanges", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ExecuteSql", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AddAsync", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ApiClientId", dto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ApiSecret", dto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CallbackSecret", dto, StringComparison.OrdinalIgnoreCase);
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
