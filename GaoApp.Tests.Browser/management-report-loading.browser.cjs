'use strict';
const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path');
const { chromium } = require('playwright');
(async () => {
  const info = JSON.parse(await new Promise(resolve => { let text = ''; process.stdin.on('data', v => text += v); process.stdin.on('end', () => resolve(text)); }));
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  try {
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
    const errors = []; page.on('pageerror', e => errors.push(e.message));
    const api = '**/admin/reports/management/data?**';
    const loaded = async () => {
      await page.waitForFunction(() => document.querySelector('[data-management-report]')?.getAttribute('aria-busy') === 'false');
      assert.equal(await page.locator('[data-report-error]').isVisible(), false, await page.locator('[data-report-error]').innerText());
      assert.equal(await page.locator('[data-report-results]').isVisible(), true);
    };
    const refresh = async () => { await page.locator('[data-report-refresh]').click(); };
    await page.goto(info.baseUrl + '/admin/account/login');
    await page.locator('[name=UserName]').fill(info.user); await page.locator('[name=Password]').fill(info.password);
    await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
    await Promise.all([page.waitForURL(u => !u.pathname.endsWith('/login')), page.locator('#loginForm button[type=submit]').click()]);
    const initial = page.waitForResponse(r => r.url().includes('/management/data?'));
    await page.goto(info.baseUrl + '/admin/reports/overview?fromDate=' + info.day + '&toDate=' + info.day + '&compare=none');
    const response = await initial; assert.equal(new URL(response.url()).searchParams.get('view'), 'overview');
    const data = await response.json(); assert.equal(data.current.summary.netSales.value, 60);
    assert.equal(data.current.products.length, 1); assert.equal(data.current.categories.length, 0);
    await loaded(); assert.equal(await page.locator('.mr-kpi').count(), 4);
    const missing = structuredClone(data);
    missing.current.summary.cogs = { value: null, available: false, quality: 3 };
    missing.current.summary.grossProfit = { value: null, available: false, quality: 3 };
    missing.current.costDiagnostics = { affectedOrders: 1, affectedLines: 1, unavailableOrders: 1,
      conflictingOrders: 0, totalIssues: 1, issues: [{orderId: 123, orderNumber: 'POS-NEEDS-COST',
        itemName: 'Aquafina <img src=x onerror=alert(1)>', message: 'Chưa có bút toán giá vốn xuất bán cho dòng hàng này.'}] };
    await page.route(api, route => route.fulfill({contentType:'application/json', body:JSON.stringify(missing)}));
    await refresh(); await loaded();
    await page.locator('[data-report-cost-diagnostics] summary').click();
    assert.match(await page.locator('[data-report-cost-diagnostics]').innerText(), /1 đơn · 1 dòng hàng.*POS-NEEDS-COST/s);
    assert.equal(await page.locator('[data-report-cost-diagnostics] img').count(), 0);
    const diagnosticLink = new URL(await page.locator('[data-report-cost-diagnostics] a').last().getAttribute('href'),info.baseUrl);
    assert.equal(diagnosticLink.searchParams.get('segment'),'issues');
    assert.equal(diagnosticLink.searchParams.get('compare'),'none');
    fs.mkdirSync(info.evidenceRoot, {recursive:true});
    await page.screenshot({path:path.join(info.evidenceRoot,'management-report-cost-diagnostics-verified.png'),fullPage:true});
    await page.unroute(api); await refresh(); await loaded();
    assert.equal(await page.locator('[data-report-cost-diagnostics]').isVisible(), false);
    const normalTimeout = Number(await page.locator('[data-management-report]').getAttribute('data-request-timeout-ms'));
    assert.ok(normalTimeout >= 11000);
    await page.evaluate(() => document.querySelector('[data-management-report]').dataset.requestTimeoutMs = '1000');

    let release;
    await page.route(api, async route => {
      await new Promise(resolve => { release = resolve; });
      try { await route.fulfill({ contentType: 'application/json', body: JSON.stringify(data) }); } catch (_) {}
    });
    await refresh(); await page.waitForSelector('[data-report-error]:visible');
    assert.match(await page.locator('[data-report-error]').innerText(), /sau 1 giây/);
    assert.equal(await page.locator('[data-report-live]').isVisible(), false);
    assert.equal(await page.locator('[data-management-report]').getAttribute('aria-busy'), 'false');
    release(); await page.unroute(api); await page.locator('[data-report-retry]').click(); await loaded();

    await page.route(api, route => route.fulfill({ status: 500, contentType: 'application/json', body: JSON.stringify({ message: 'Lỗi kiểm thử', traceId: 'REPORT-PROBE-500' }) }));
    await refresh(); await page.waitForSelector('[data-report-error]:visible');
    assert.match(await page.locator('[data-report-error]').innerText(), /HTTP 500.*REPORT-PROBE-500/);
    await page.unroute(api);
    await page.route(api, route => route.fulfill({ contentType: 'text/html', body: '<html>Login</html>' }));
    await refresh(); await page.waitForSelector('[data-report-error]:visible');
    assert.match(await page.locator('[data-report-error]').innerText(), /Server không trả dữ liệu báo cáo hợp lệ/);
    await page.unroute(api);

    // A delayed response from an abandoned filter must never overwrite the latest report.
    let oldRequest, observeOld, calls = 0;
    const oldObserved = new Promise(resolve => { observeOld = resolve; });
    const replacement = structuredClone(data); replacement.current.summary.netSales.value = 999;
    await page.route(api, async route => {
      if (++calls === 1) { await new Promise(resolve => { oldRequest = resolve; observeOld(); }); try { await route.fulfill({ contentType: 'application/json', body: JSON.stringify(data) }); } catch (_) {} }
      else await route.fulfill({ contentType: 'application/json', body: JSON.stringify(replacement) });
    });
    await refresh(); await oldObserved;
    await refresh(); await loaded(); oldRequest(); await page.unroute(api);
    assert.match(await page.locator('[data-report-kpis]').innerText(), /999/);

    // Disable observers only while this page script initializes; Playwright itself needs the
    // native MutationObserver in its isolated world for DOM inspection.
    await page.route('**/Admin/js/reports/management-report.page.js*', async route => {
      const response = await route.fetch();
      const script = await response.text();
      await route.fulfill({ response, body: '(()=>{const resize=window.ResizeObserver,mutation=window.MutationObserver;window.ResizeObserver=undefined;window.MutationObserver=undefined;try{\n' + script + '\n}finally{window.ResizeObserver=resize;window.MutationObserver=mutation;}})();' });
    });
    await page.reload(); await loaded();
    const full = page.waitForResponse(r => r.url().includes('/management/data?'));
    await page.locator('[data-report-nav=products]').click();
    assert.equal(new URL((await full).url()).searchParams.has('view'), false); await loaded();
    assert.match(await page.locator('[data-report-panels]').innerText(), /Sản phẩm|sản phẩm/);
    fs.mkdirSync(info.evidenceRoot, { recursive: true });
    await page.screenshot({ path: path.join(info.evidenceRoot, 'management-report-loading-verified.png'), fullPage: true });
    assert.deepEqual(errors, []);
    console.log('PASS: compact overview, full products view, charts, timeout/retry, HTTP error trace, non-JSON response, obsolete request and missing observers.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
