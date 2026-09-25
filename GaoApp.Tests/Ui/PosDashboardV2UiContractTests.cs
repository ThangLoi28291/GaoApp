namespace GaoApp.Tests.Ui;

public sealed class PosDashboardV2UiContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string ViewPath = Path.Combine(
        RepositoryRoot, "GaoApp.Web", "Areas", "Admin", "Views", "POS", "Dashboard.cshtml");
    private static readonly string StylePath = Path.Combine(
        RepositoryRoot, "GaoApp.Web", "wwwroot", "Admin", "css", "pos", "pos-dashboard.css");
    private static readonly string ScriptPath = Path.Combine(
        RepositoryRoot, "GaoApp.Web", "wwwroot", "Admin", "js", "pos", "pos.dashboard.page.js");

    [Fact]
    public void Dashboard_should_use_external_assets_and_approved_information_hierarchy()
    {
        var view = File.ReadAllText(ViewPath);

        Assert.Contains("data-pos-dashboard-v2", view, StringComparison.Ordinal);
        Assert.Contains("~/Admin/css/pos/pos-dashboard.css", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("~/Admin/js/pos/pos.dashboard.page.js", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<style>", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("shift-hero-card", view, StringComparison.Ordinal);

        Assert.Contains("Giá trị đơn hoàn tất", view, StringComparison.Ordinal);
        Assert.Contains("Đơn hoàn tất", view, StringComparison.Ordinal);
        Assert.Contains("Giá trị TB đơn hoàn tất", view, StringComparison.Ordinal);
        Assert.Contains("Tiền mặt dự kiến tại quầy", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Tổng doanh thu", view, StringComparison.Ordinal);

        Assert.Contains("Giá trị đơn hoàn tất theo giờ", view, StringComparison.Ordinal);
        Assert.Contains("Đơn hoàn tất theo giờ", view, StringComparison.Ordinal);
        Assert.Contains("Cơ cấu tiền bán", view, StringComparison.Ordinal);
        Assert.Contains("Ca &amp; dòng tiền", view, StringComparison.Ordinal);
        Assert.Contains("CẦN CHÚ Ý", view, StringComparison.Ordinal);
    }

    [Fact]
    public void Dashboard_should_expose_live_loading_error_no_shift_and_drill_down_contracts()
    {
        var view = File.ReadAllText(ViewPath);

        Assert.Contains("id=\"dashboardSkeleton\"", view, StringComparison.Ordinal);
        Assert.Contains("id=\"dashboardStatePanel\"", view, StringComparison.Ordinal);
        Assert.Contains("id=\"dashboardContent\"", view, StringComparison.Ordinal);
        Assert.Contains("id=\"dashboardLiveStatus\"", view, StringComparison.Ordinal);
        Assert.Contains("id=\"btnRefreshDashboard\"", view, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", view, StringComparison.Ordinal);
        Assert.Contains("aria-busy=\"true\"", view, StringComparison.Ordinal);

        Assert.Contains("/admin/pos/orders-page", view, StringComparison.Ordinal);
        Assert.Contains("/admin/pos-shift", view, StringComparison.Ordinal);
        Assert.Contains("/admin/pos-shift/history", view, StringComparison.Ordinal);
    }

    [Fact]
    public void Dashboard_should_use_only_approved_read_boundaries_for_P1_data()
    {
        var view = File.ReadAllText(ViewPath);
        var script = File.ReadAllText(ScriptPath);

        Assert.Contains("/admin/pos/shift/dashboard", view, StringComparison.Ordinal);
        Assert.Contains("/admin/pos/shift/summary?shiftId={shiftId}", view, StringComparison.Ordinal);

        Assert.Contains("AbortController", script, StringComparison.Ordinal);
        Assert.Contains("requestSequence", script, StringComparison.Ordinal);
        Assert.Contains("lastGoodData", script, StringComparison.Ordinal);
        Assert.Contains("REFRESH_INTERVAL_MS = 10000", script, StringComparison.Ordinal);
        Assert.Contains("method: 'GET'", script, StringComparison.Ordinal);

        Assert.DoesNotContain("method: 'POST'", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("method: 'PUT'", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("method: 'PATCH'", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("method: 'DELETE'", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("setInterval", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Dashboard_assets_should_be_scoped_responsive_accessible_and_dependency_free()
    {
        Assert.True(File.Exists(StylePath), $"Missing dashboard stylesheet: {StylePath}");
        Assert.True(File.Exists(ScriptPath), $"Missing dashboard script: {ScriptPath}");

        var style = File.ReadAllText(StylePath);
        var script = File.ReadAllText(ScriptPath);

        Assert.Contains(".pos-dashboard-v2", style, StringComparison.Ordinal);
        Assert.Contains("max-width: none", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("padding-left: 24px", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("padding-left: 16px", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@media (max-width: 767.98px)", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("prefers-reduced-motion", style, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("buildHourlyBuckets", script, StringComparison.Ordinal);
        Assert.Contains("renderCompletedSalesChart", script, StringComparison.Ordinal);
        Assert.Contains("renderCompletedOrdersChart", script, StringComparison.Ordinal);
        Assert.Contains("renderPaymentMix", script, StringComparison.Ordinal);
        Assert.Contains("role=\"img\"", script, StringComparison.Ordinal);
        Assert.Contains("aria-label", script, StringComparison.Ordinal);

        Assert.DoesNotContain("Chart.js", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ApexCharts", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ECharts", script, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("GAOAPP_REPOSITORY_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot) &&
            File.Exists(Path.Combine(configuredRoot, "GaoApp.sln")))
        {
            return configuredRoot;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate GaoApp repository root.");
    }
}
