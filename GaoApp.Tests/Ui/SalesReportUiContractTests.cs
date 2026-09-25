using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GaoApp.Tests.Ui;

public sealed class SalesReportUiContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string ViewPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "Areas",
        "Admin",
        "Views",
        "SalesExecutiveReport",
        "Index.cshtml");

    private static readonly string ControllerPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "Areas",
        "Admin",
        "Controllers",
        "SalesExecutiveReportController.cs");

    private static readonly string ScriptPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "wwwroot",
        "Admin",
        "js",
        "reports",
        "sales-report.page.js");

    private static readonly string StylePath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Web",
        "wwwroot",
        "Admin",
        "css",
        "reports",
        "sales-report.css");

    private static readonly string RepositoryPath = Path.Combine(
        RepositoryRoot,
        "GaoApp.Infrastructure",
        "Repositories",
        "Reports",
        "SalesReportReadRepository.cs");

    [Fact]
    public void Executive_view_should_expose_approved_sales_first_hierarchy_without_profit_scope()
    {
        var view = File.ReadAllText(ViewPath);

        Assert.Contains("Tổng quan kinh doanh", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Net Sales", view, StringComparison.Ordinal);
        Assert.Contains("Đơn bán", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AOV", view, StringComparison.Ordinal);
        Assert.Contains("Discounts", view, StringComparison.Ordinal);
        Assert.Contains("Returns", view, StringComparison.Ordinal);
        Assert.Contains("Refund Amount", view, StringComparison.Ordinal);
        Assert.Contains("Sales Bridge", view, StringComparison.Ordinal);
        Assert.Contains("Đơn đã Void trong kỳ bán", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-sales-executive-report", view, StringComparison.Ordinal);
        Assert.Contains("id=\"salesReportSkeleton\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"salesReportStaleBanner\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"salesReportStatePanel\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"salesTrendDataBody\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sales by Hour", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Top Products", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Discount Breakdown", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Returns vs Refunds", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Customer attachment", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"salesByHourDataBody\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"salesTopProductsDataBody\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"salesDiscountDataBody\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"salesReturnsRefundsDataBody\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"salesCustomerMixDataBody\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Gross Sales + số lượng theo đơn vị gốc", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("không phải Repeat Rate", view, StringComparison.OrdinalIgnoreCase);

        // RPT-2A permits a Profit navigation link in the header; Sales metrics remain sales-only.
        var reportStart = view.IndexOf("data-sales-executive-report", StringComparison.Ordinal);
        Assert.True(reportStart >= 0, "Sales report content boundary must exist.");
        var reportContent = view[reportStart..];
        Assert.DoesNotContain("Gross Profit", reportContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("COGS", reportContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Lợi nhuận", reportContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Executive_view_should_use_local_scoped_assets_without_cdn()
    {
        var view = File.ReadAllText(ViewPath);

        Assert.Contains("~/vendor/libs/apex-charts/apexcharts.js", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("~/vendor/libs/apex-charts/apex-charts.css", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("~/Admin/css/reports/sales-report.css", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("~/Admin/js/reports/sales-report.page.js", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cdn.jsdelivr", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Controller_and_repository_should_remain_permissioned_get_only_and_read_only()
    {
        var controller = File.ReadAllText(ControllerPath);
        var repository = File.ReadAllText(RepositoryPath);

        Assert.Contains("admin/reports/overview", controller, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PermissionCodes.Report.Sales.View", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpGet", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("[HttpPost", controller, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[HttpPut", controller, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[HttpDelete", controller, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("AsNoTracking", repository, StringComparison.Ordinal);
        Assert.Contains("order.StoreId == storeId", repository, StringComparison.Ordinal);
        Assert.Contains("salesReturn.StoreId == storeId", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChanges", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ExecuteSql", repository, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Page_script_should_preserve_url_state_latest_request_and_stale_data_behavior()
    {
        var script = File.ReadAllText(ScriptPath);
        var style = File.ReadAllText(StylePath);

        Assert.Contains("AbortController", script, StringComparison.Ordinal);
        Assert.Contains("requestSequence", script, StringComparison.Ordinal);
        Assert.Contains("URLSearchParams", script, StringComparison.Ordinal);
        Assert.Contains("history.replaceState", script, StringComparison.Ordinal);
        Assert.Contains("lastGoodData", script, StringComparison.Ordinal);
        Assert.Contains("salesReportStaleBanner", script, StringComparison.Ordinal);
        Assert.Contains("ApexCharts", script, StringComparison.Ordinal);
        Assert.Contains("salesTrendDataBody", script, StringComparison.Ordinal);
        Assert.Contains("renderSalesByHour", script, StringComparison.Ordinal);
        Assert.Contains("renderTopProducts", script, StringComparison.Ordinal);
        Assert.Contains("renderDiscountBreakdown", script, StringComparison.Ordinal);
        Assert.Contains("renderReturnsRefunds", script, StringComparison.Ordinal);
        Assert.Contains("renderCustomerMix", script, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-motion", script, StringComparison.OrdinalIgnoreCase);

        Assert.Contains(".sales-report-kpi-grid", style, StringComparison.Ordinal);
        Assert.Contains(".sales-report-main-grid", style, StringComparison.Ordinal);
        Assert.Contains(".sales-report-insights-grid", style, StringComparison.Ordinal);
        Assert.Contains(".sales-report-customer-summary", style, StringComparison.Ordinal);
        Assert.Contains("@media", style, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("prefers-reduced-motion", style, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sales_detail_should_expose_get_only_segments_drilldown_and_report_menu_contract()
    {
        var detailController = File.ReadAllText(Path.Combine(RepositoryRoot, "GaoApp.Web", "Areas", "Admin", "Controllers", "SalesReportController.cs"));
        var detailView = File.ReadAllText(Path.Combine(RepositoryRoot, "GaoApp.Web", "Areas", "Admin", "Views", "SalesReport", "Index.cshtml"));
        var menu = File.ReadAllText(Path.Combine(RepositoryRoot, "GaoApp.Infrastructure", "Data", "Seed", "AdminMenuSeeder.cs"));
        var script = File.ReadAllText(ScriptPath);

        Assert.Contains("admin/reports/sales", detailController, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PermissionCodes.Report.Sales.View", detailController, StringComparison.Ordinal);
        Assert.DoesNotContain("[HttpPost", detailController, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Đơn bán", detailView, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sản phẩm", detailView, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Trả hàng & hoàn tiền", detailView, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Đơn đã Void trong kỳ bán", detailView + script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-order-detail-base-url", detailView, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bucketIndex", script, StringComparison.Ordinal);
        Assert.Contains("variantId", script, StringComparison.Ordinal);
        Assert.Contains("/admin/reports/overview", menu, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/admin/reports/sales", menu, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PermissionCodes.Report.Sales.View", menu, StringComparison.Ordinal);
    }

    [Fact]
    public void Sales_detail_overview_links_should_start_with_only_shared_report_filters()
    {
        var view = File.ReadAllText(Path.Combine(RepositoryRoot, "GaoApp.Web", "Areas", "Admin", "Views", "SalesReport", "Index.cshtml"));
        var route = Regex.Match(view, @"var overviewUrl = Url\.Action\([\s\S]*?\}\);").Value;
        Assert.NotEmpty(route);
        var keys = Regex.Matches(route, "Context.Request.Query\\[\"([^\"]+)\"\\]")
            .Select(match => match.Groups[1].Value).OrderBy(key => key).ToArray();
        Assert.Equal(new[] { "compare", "customerState", "fromDate", "terminalId", "toDate" }, keys);
        Assert.Equal(2, Regex.Matches(view, "data-sales-overview-link href=\"@overviewUrl\"").Count);
    }

    [Fact]
    public async Task Sales_detail_refresh_should_fail_closed_and_preserve_only_shared_overview_filters()
    {
        var harness = $$"""
            const fs = require('fs');
            const vm = require('vm');
            const assert = require('node:assert/strict');
            const source = fs.readFileSync({{JsonSerializer.Serialize(ScriptPath)}}, 'utf8');
            class Element {
                constructor() {
                    this.value = ''; this.hidden = true; this.children = []; this.dataset = {};
                    this.handlers = {}; this.attributes = {}; this.classList = { toggle() {} };
                }
                addEventListener(type, handler) { this.handlers[type] = handler; }
                setAttribute(key, value) { this.attributes[key] = value; }
                append(...children) { this.children.push(...children); }
                replaceChildren(...children) { this.children = children; }
            }
            const settle = () => new Promise(resolve => setImmediate(resolve));
            async function scenario(segment) {
                const elements = {};
                const get = id => elements[id] ||= new Element();
                get('salesDetailOverview').querySelectorAll = () => [get('salesDetailOverviewNet')];
                const root = new Element();
                root.dataset = {
                    contextUrl: '/context', ordersUrl: '/orders', overviewDataUrl: '/overview',
                    orderDetailBaseUrl: '/admin/pos/order-detail'
                };
                root.querySelectorAll = () => [];
                const links = [new Element(), new Element()];
                links.forEach(link => link.href = '/admin/reports/overview');
                const location = { origin: 'https://review.invalid', pathname: '/admin/reports/sales',
                    search: '?segment=' + segment + '&fromDate=2026-09-01&toDate=2026-09-01&compare=none&terminalId=7&customerState=linked&page=1&pageSize=25&search=synthetic&sort=newest&variantId=9&bucketIndex=0&focus=refund' };
                const requests = [];
                let failContext = false;
                const pending = [];
                let deferContext = false;
                const contextResponse = url => ({ ok: true, json: async () => ({
                    query: { fromDate: url.searchParams.get('fromDate'), toDate: url.searchParams.get('toDate'), terminalId: 7, customerState: 'linked' },
                    terminals: [{ id: 7, code: 'POS07', name: 'Synthetic' }]
                }) });
                const sandbox = {
                    URL, URLSearchParams, Intl, Date, Number, Math, DOMException, AbortController,
                    document: {
                        querySelector: selector => selector === '[data-sales-detail-report]' ? root : null,
                        querySelectorAll: () => links, getElementById: get, createElement: () => new Element()
                    },
                    location, history: { replaceState(a, b, url) { location.search = new URL(url, location.origin).search; } },
                    Option: function(text, value) { this.text = text; this.value = value; },
                    fetch: async url => {
                        requests.push(url.pathname);
                        if (url.pathname === '/context') {
                            if (deferContext) return new Promise(resolve => pending.push({ resolve, url }));
                            return failContext ? { ok: false, status: 500 } : contextResponse(url);
                        }
                        if (url.pathname === '/overview') return { ok: true, json: async () => ({ current: { summary: { netSales: 100 }, bridge: { netSales: 100 } } }) };
                        return { ok: true, json: async () => ({ items: [{ orderId: 1, orderNumber: 'OLD-PERIOD', customerName: 'Synthetic', subtotal: 100, discounts: 0, salesAfterDiscount: 100 }], page: 1, totalPages: 1, totalItems: 1 }) };
                    }
                };
                sandbox.window = { location, addEventListener() {} };
                vm.createContext(sandbox);
                vm.runInContext(source, sandbox);
                await settle();
                assert.equal(root.attributes['aria-busy'], 'false');
                assert.equal(segment === 'overview' ? get('salesDetailOverview').hidden : get('salesDetailTableWrap').hidden, false);
                const resultRequests = () => requests.filter(path => path !== '/context').length;
                const before = resultRequests();
                failContext = true;
                get('salesDetailFromDate').value = '2026-09-02';
                get('salesDetailToDate').value = '2026-09-02';
                get('salesDetailFromDate').handlers.change();
                assert.equal(get('salesDetailTableWrap').hidden, true);
                assert.equal(get('salesDetailOverview').hidden, true);
                assert.equal(get('salesDetailTableBody').children.length, 0);
                assert.equal(get('salesDetailOverviewNet').textContent, '—');
                await settle();
                assert.equal(resultRequests(), before, 'failed context must not load segment');
                assert.equal(get('salesDetailState').hidden, false);
                assert.equal(get('salesDetailDataSection').hidden, false, 'error must be visible even from overview');
                assert.equal(get('salesDetailStateTitle').textContent, 'Không tải được dữ liệu');
                assert.equal(root.attributes['aria-busy'], 'false');
                assert.equal(get('salesDetailLoading').hidden, true);
                assert.equal(get('salesDetailNext').disabled, true);
                for (const link of links) {
                    const q = new URL(link.href, location.origin).searchParams;
                    assert.deepEqual([...q.keys()].sort(), ['compare', 'customerState', 'fromDate', 'terminalId', 'toDate']);
                    assert.equal(q.get('fromDate'), '2026-09-02');
                    assert.equal(q.get('toDate'), '2026-09-02');
                    assert.equal(q.get('compare'), 'none');
                    assert.equal(q.get('terminalId'), '7');
                    assert.equal(q.get('customerState'), 'linked');
                }
                failContext = false;
                await get('salesDetailRefreshButton').handlers.click();
                assert.equal(resultRequests(), before + 1, 'manual retry must recover');
                assert.equal(segment === 'overview' ? get('salesDetailOverview').hidden : get('salesDetailTableWrap').hidden, false);
                deferContext = true;
                const first = get('salesDetailRefreshButton').handlers.click();
                const second = get('salesDetailRefreshButton').handlers.click();
                pending[0].resolve(contextResponse(pending[0].url));
                await first;
                assert.equal(root.attributes['aria-busy'], 'true', 'superseded refresh must not end latest busy state');
                assert.equal(resultRequests(), before + 1);
                pending[1].resolve(contextResponse(pending[1].url));
                await second;
                assert.equal(root.attributes['aria-busy'], 'false');
                assert.equal(resultRequests(), before + 2);
            }
            (async () => {
                await scenario('orders');
                await scenario('overview');
                console.log('SALES_DETAIL_REMEDIATION_PASS');
            })().catch(error => { console.error(error); process.exitCode = 1; });
            """;
        var startInfo = new ProcessStartInfo("node")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-");
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start Node.js.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(harness);
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        var output = await outputTask;
        var error = await errorTask;
        Assert.True(process.ExitCode == 0, $"Sales Detail behavior failed. stdout={output} stderr={error}");
        Assert.Contains("SALES_DETAIL_REMEDIATION_PASS", output, StringComparison.Ordinal);
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
