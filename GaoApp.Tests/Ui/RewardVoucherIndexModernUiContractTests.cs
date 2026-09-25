using Xunit;

namespace GaoApp.Tests.Ui;

public sealed class RewardVoucherIndexModernUiContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string IndexPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "Areas", "Admin", "Views", "RewardVouchers", "Index.cshtml");

    private static readonly string ScriptPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "wwwroot", "Admin", "js", "reward-vouchers.js");

    private static readonly string StylePath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "wwwroot", "Admin", "css", "reward-voucher-index.css");

    private static readonly string ControllerPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "Areas", "Admin", "Controllers", "RewardVouchersController.cs");

    private static readonly string RepositoryPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Infrastructure", "Repositories", "Rewards", "RewardVoucherIndexReadRepository.cs");

    private static readonly string DtoPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Application", "DTOs", "Rewards", "Vouchers", "RewardVoucherIndexReadDtos.cs");

    private static readonly string ServicePath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Application", "Services", "Rewards", "RewardVoucherIndexReadService.cs");

    [Fact]
    public void Index_should_expose_approved_kpis_filters_table_quick_view_and_mobile_surfaces()
    {
        var view = File.ReadAllText(IndexPath);

        Assert.Contains("ViewData[\"container\"] = \"container-fluid\"", view, StringComparison.Ordinal);
        Assert.Contains("data-gao-ui=\"modern\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-reward-voucher-index", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("row-cols-xl-4", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-voucher-kpi=\"total\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-voucher-kpi=\"available\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-voucher-kpi=\"used\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-voucher-kpi=\"unavailable\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"rewardVoucherKeyword\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"rewardVoucherStatus\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"rewardVoucherFromDate\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"rewardVoucherToDate\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"rewardVoucherPageSize\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"rewardVoucherDesktopList\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"rewardVoucherMobileList\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"rewardVoucherQuickView\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"rewardVoucherMobileFilterSheet\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"rewardVoucherHistorySheet\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"rewardVoucherConfirmation\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@Html.AntiForgeryToken()", view, StringComparison.Ordinal);
        Assert.DoesNotContain(">ID<", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Interaction_should_be_realtime_latest_request_safe_and_preserve_existing_actions()
    {
        var script = File.ReadAllText(ScriptPath);

        Assert.Contains("AbortController", script, StringComparison.Ordinal);
        Assert.Contains("rewardVoucherIndexRequestSequence", script, StringComparison.Ordinal);
        Assert.Contains("rewardVoucherSearchTimer", script, StringComparison.Ordinal);
        Assert.Contains("350", script, StringComparison.Ordinal);
        Assert.Contains("renderRewardVoucherDesktopRows", script, StringComparison.Ordinal);
        Assert.Contains("renderRewardVoucherMobileCards", script, StringComparison.Ordinal);
        Assert.Contains("renderRewardVoucherCircularPagination", script, StringComparison.Ordinal);
        Assert.Contains("openRewardVoucherQuickView", script, StringComparison.Ordinal);
        Assert.Contains("showRewardVoucherConfirmation", script, StringComparison.Ordinal);
        Assert.Contains("/admin/reward-vouchers/data", script, StringComparison.Ordinal);
        Assert.Contains("/admin/api/customers/reward-vouchers", script, StringComparison.Ordinal);
        Assert.Contains("/cancel", script, StringComparison.Ordinal);
        Assert.Contains("/lock", script, StringComparison.Ordinal);
        Assert.Contains("/unlock", script, StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", script, StringComparison.Ordinal);
        Assert.DoesNotContain("window.prompt", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("window.alert", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Module_styles_should_scope_desktop_mobile_sheets_actions_and_safe_area()
    {
        var style = File.ReadAllText(StylePath);

        Assert.Contains(".reward-voucher-index-page.gds-page", style, StringComparison.Ordinal);
        Assert.Contains(".reward-voucher-desktop-list", style, StringComparison.Ordinal);
        Assert.Contains(".reward-voucher-mobile-list", style, StringComparison.Ordinal);
        Assert.Contains(".reward-voucher-mobile-card", style, StringComparison.Ordinal);
        Assert.Contains(".reward-voucher-filter-sheet", style, StringComparison.Ordinal);
        Assert.Contains(".reward-voucher-quick-sheet", style, StringComparison.Ordinal);
        Assert.Contains(".reward-voucher-history-sheet", style, StringComparison.Ordinal);
        Assert.Contains(".reward-voucher-confirmation-sheet", style, StringComparison.Ordinal);
        Assert.Contains(".reward-voucher-action", style, StringComparison.Ordinal);
        Assert.Contains("overflow-x: hidden", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("overflow-wrap", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("env(safe-area-inset-bottom)", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@media", style, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Controller_should_add_customer_view_protected_store_scoped_get_only_reads()
    {
        var controller = File.ReadAllText(ControllerPath);

        Assert.Contains("BaseAdminController", controller, StringComparison.Ordinal);
        Assert.Contains("PermissionCodes.Catalog.Customer.View", controller, StringComparison.Ordinal);
        Assert.Contains("IRewardVoucherIndexReadService", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"data\")]", controller, StringComparison.Ordinal);
        Assert.Contains("GetRewardVoucherIndexData", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"{id:int}/quick-view\")]", controller, StringComparison.Ordinal);
        Assert.Contains("GetRewardVoucherQuickView", controller, StringComparison.Ordinal);
        Assert.Contains("CurrentStoreId", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("[HttpPost", controller, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_stack_should_apply_exact_scope_summary_search_filters_and_no_write_behavior()
    {
        var repository = File.ReadAllText(RepositoryPath);
        var dto = File.ReadAllText(DtoPath);
        var service = File.ReadAllText(ServicePath);

        Assert.Contains("Latin1_General_100_CI_AI", repository, StringComparison.Ordinal);
        Assert.Contains("AsNoTracking", repository, StringComparison.Ordinal);
        Assert.Contains("StoreId == storeId", repository, StringComparison.Ordinal);
        Assert.Contains("TotalVouchers", repository, StringComparison.Ordinal);
        Assert.Contains("AvailableVouchers", repository, StringComparison.Ordinal);
        Assert.Contains("UsedVouchers", repository, StringComparison.Ordinal);
        Assert.Contains("UnavailableVouchers", repository, StringComparison.Ordinal);
        Assert.Contains("ApplyKeyword", repository, StringComparison.Ordinal);
        Assert.Contains("ApplyIssuedDateRange", repository, StringComparison.Ordinal);
        Assert.Contains("ApplyStatus", repository, StringComparison.Ordinal);
        Assert.Contains("CustomerRewardVoucherStatus.Cancelled", repository, StringComparison.Ordinal);
        Assert.Contains("CustomerRewardVoucherStatus.Expired", repository, StringComparison.Ordinal);
        Assert.Contains("CustomerRewardVoucherStatus.Locked", repository, StringComparison.Ordinal);
        Assert.Contains("RewardVoucherIndexSummaryDto", dto, StringComparison.Ordinal);
        Assert.Contains("RewardVoucherIndexStatuses.All", service, StringComparison.Ordinal);
        Assert.DoesNotContain("UsedOrderId", dto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SaveChanges", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ExecuteUpdate", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ExecuteDelete", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AddAsync", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Remove", repository, StringComparison.OrdinalIgnoreCase);
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
