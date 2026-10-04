using Xunit;

namespace GaoApp.Tests.Ui;

public sealed class InventoryLedgerIndexModernUiContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string IndexPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "Areas", "Admin", "Views", "InventoryLedger", "Index.cshtml");

    private static readonly string ScriptPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "wwwroot", "Admin", "js", "inventory-ledger.js");

    private static readonly string StylePath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "wwwroot", "Admin", "css", "inventory-ledger.css");

    private static readonly string ControllerPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web", "Areas", "Admin", "Controllers", "InventoryLedgerController.cs");

    private static readonly string RepositoryPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Infrastructure", "Repositories", "Inventory", "InventoryLedgerIndexReadRepository.cs");

    private static readonly string TransactionConfigurationPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Infrastructure", "Data", "Configurations", "InventoryTransactionConfiguration.cs");

    private static readonly string TimelineMigrationPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Infrastructure", "Migrations", "20260920093000_OptimizeInventoryLedgerTimeline.cs");

    [Fact]
    public void Index_should_expose_the_approved_read_only_desktop_and_mobile_hierarchy()
    {
        var view = File.ReadAllText(IndexPath);

        Assert.Contains("ViewData[\"container\"] = \"container-fluid\"", view, StringComparison.Ordinal);
        Assert.Contains("data-gao-ui=\"modern\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-inventory-ledger-index", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("row-cols-xl-4", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"ledgerKeyword\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"ledgerWarehouseFilter\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"ledgerTransactionTypeFilter\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"ledgerReferenceTypeFilter\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"ledgerPageSize\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"ledgerDesktopList\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"ledgerMobileList\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"ledgerQuickViewModal\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"ledgerMobileFilterSheet\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("~/Admin/css/inventory-ledger.css", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("id=\"ProductVariantId\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ReferenceLineId", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<style", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Interaction_should_support_latest_request_date_only_images_quick_view_and_vietnamese_labels()
    {
        var script = File.ReadAllText(ScriptPath);

        Assert.Contains("AbortController", script, StringComparison.Ordinal);
        Assert.Contains("requestSequence", script, StringComparison.Ordinal);
        Assert.Contains("searchDebounceTimer", script, StringComparison.Ordinal);
        Assert.Contains("renderLedgerDesktopRows", script, StringComparison.Ordinal);
        Assert.Contains("renderLedgerMobileCards", script, StringComparison.Ordinal);
        Assert.Contains("renderLedgerPagination", script, StringComparison.Ordinal);
        Assert.Contains("openLedgerQuickView", script, StringComparison.Ordinal);
        Assert.Contains("ledgerImageHoverPreview", script, StringComparison.Ordinal);
        Assert.Contains("pointerenter", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dblclick", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("toLocaleDateString(\"vi-VN\")", script, StringComparison.Ordinal);
        Assert.Contains("Số dư đầu kỳ", script, StringComparison.Ordinal);
        Assert.Contains("Nhập mua hàng", script, StringComparison.Ordinal);
        Assert.Contains("Chứng từ kho", script, StringComparison.Ordinal);
        Assert.Contains("Định giá lại", script, StringComparison.Ordinal);
        Assert.Contains("data-page", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Module_styles_should_provide_compact_table_mobile_timeline_sheets_and_no_overflow()
    {
        var style = File.ReadAllText(StylePath);

        Assert.Contains(".ledger-desktop-list", style, StringComparison.Ordinal);
        Assert.Contains(".ledger-mobile-list", style, StringComparison.Ordinal);
        Assert.Contains(".ledger-mobile-card", style, StringComparison.Ordinal);
        Assert.Contains(".ledger-filter-sheet", style, StringComparison.Ordinal);
        Assert.Contains(".ledger-image-hover-preview", style, StringComparison.Ordinal);
        Assert.Contains(".inventory-ledger-page.gds-page", style, StringComparison.Ordinal);
        Assert.Contains("overflow-x: hidden", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("env(safe-area-inset-bottom)", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@media", style, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Controller_should_use_the_isolated_authenticated_read_service()
    {
        var controller = File.ReadAllText(ControllerPath);

        Assert.Contains("[Authorize]", controller, StringComparison.Ordinal);
        Assert.Contains("IInventoryLedgerIndexReadService", controller, StringComparison.Ordinal);
        Assert.Contains("GetData", controller, StringComparison.Ordinal);
        Assert.Contains("GetQuickView", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"data\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"quick-view\")]", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChanges", controller, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_repository_should_preserve_ledger_identity_math_tenant_scope_and_image_precedence()
    {
        var repository = File.ReadAllText(RepositoryPath);

        Assert.Contains("Latin1_General_100_CI_AI", repository, StringComparison.Ordinal);
        Assert.Contains("AsNoTracking", repository, StringComparison.Ordinal);
        Assert.Contains("QuantityChange > 0", repository, StringComparison.Ordinal);
        Assert.Contains("QuantityChange < 0", repository, StringComparison.Ordinal);
        Assert.Contains("AfterQty < 0", repository, StringComparison.Ordinal);
        Assert.Contains("OccurredAtUtc", repository, StringComparison.Ordinal);
        Assert.Contains("PrimaryProductImage", repository, StringComparison.Ordinal);
        Assert.Contains("ProductImages", repository, StringComparison.Ordinal);
        Assert.Contains("StoreId == storeId", repository, StringComparison.Ordinal);
        foreach (var field in new[] { "UnitCostSnapshot", "TotalCost", "BeforeInventoryValue", "AfterInventoryValue", "RunningAverageUnitCostAfter" })
            Assert.Contains($"{field} = canViewCost && transaction.TransactionType == InventoryTransactionType.Revaluation ? transaction.{field} : (decimal?)null", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChanges", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ExecuteSql", repository, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Large_ledger_should_reuse_summary_and_have_a_filtered_timeline_index()
    {
        var script = File.ReadAllText(ScriptPath);
        var repository = File.ReadAllText(RepositoryPath);
        var configuration = File.ReadAllText(TransactionConfigurationPath);
        var migration = File.ReadAllText(TimelineMigrationPath);

        Assert.Contains("summarySignature", script, StringComparison.Ordinal);
        Assert.Contains("includeSummary", script, StringComparison.Ordinal);
        Assert.Contains("Promise.all", script, StringComparison.Ordinal);
        Assert.Contains("SummaryIncluded", repository, StringComparison.Ordinal);
        Assert.Contains("GetStateCount", repository, StringComparison.Ordinal);
        Assert.Contains("BuildAggregateQuery", repository, StringComparison.Ordinal);
        Assert.Contains("IgnoreQueryFilters", repository, StringComparison.Ordinal);
        Assert.Contains("SingleOrDefaultAsync", repository, StringComparison.Ordinal);
        Assert.Contains("IX_InventoryTransactions_LedgerTimeline", configuration, StringComparison.Ordinal);
        Assert.Contains("IsDescending(false, true, true)", configuration, StringComparison.Ordinal);
        Assert.Contains("INCLUDE ([QuantityChange], [AfterQty])", migration, StringComparison.Ordinal);
        Assert.Contains("WHERE [IsDeleted] = 0", migration, StringComparison.Ordinal);
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
