const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');

(async () => {
  const info = JSON.parse(await new Promise(resolve => { let text = ''; process.stdin.on('data', chunk => text += chunk); process.stdin.on('end', () => resolve(text)); }));
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  const context = await browser.newContext({ viewport: { width: 1480, height: 1080 }, reducedMotion: 'reduce' });
  const page = await context.newPage(), errors = [];
  page.on('pageerror', error => errors.push(error.message));
  const output = path.resolve('TestResults/operations-reports'); fs.mkdirSync(output, { recursive: true });
  async function loaded() {
    await page.waitForFunction(() => document.querySelector('[data-operations-report]')?.getAttribute('aria-busy') === 'false');
    assert.equal(await page.locator('[data-error]').isVisible(), false, await page.locator('[data-error]').innerText());
    assert.ok(await page.locator('[data-results]').isVisible());
  }
  async function go(section) { await page.goto(info.baseUrl + '/admin/reports/' + section); await loaded(); }
  async function saved(dialog) { await page.waitForFunction(selector => !document.querySelector(selector).open, dialog); await loaded(); }
  try {
    await page.goto(info.baseUrl + '/admin/account/login');
    await page.locator('[name=UserName]').fill(info.user); await page.locator('[name=Password]').fill(info.password);
    await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
    await Promise.all([page.waitForURL(url => !url.pathname.endsWith('/login')), page.locator('#loginForm button[type=submit]').click()]);
    for (const section of ['cashflow', 'inventory', 'debts']) {
      await go(section); assert.equal(await page.locator('.mr-kpi').count(), 4);
      await page.waitForSelector('.mr-chart .apexcharts-svg');
      await page.screenshot({ path: path.join(output, section + '-desktop.png'), fullPage: true });
      assert.ok(await page.locator('[data-table] tbody tr').count() > 0);
      const [download] = await Promise.all([page.waitForEvent('download'), page.locator('[data-export]').click()]);
      const file = await download.path(); assert.ok(fs.readFileSync(file, 'utf8').includes('Từ ngày'));
      await page.locator('[data-search]').fill('NO_MATCH_AT_ALL'); assert.match(await page.locator('[data-table]').innerText(), /Không có dữ liệu/);
      await page.locator('[data-search]').fill('');
    }
    // The expense page opens the actual treasury payment form with the correct source.
    const token = await page.locator('[data-operations-report] input[name=__RequestVerificationToken]').inputValue();
    const requestId = await page.evaluate(() => crypto.randomUUID());
    const expenseResponse = await page.request.post(info.baseUrl + '/admin/reports/expenses', { headers: { RequestVerificationToken: token }, data: { clientRequestId: requestId, name: 'Chi phí kiểm tra thanh toán', amount: 34, category: 'other', recognitionFrom: info.day, recognitionTo: info.day, paymentMethod: 'cash' } });
    assert.ok(expenseResponse.ok(), await expenseResponse.text()); const expense = await expenseResponse.json();
    const confirmedResponse = await page.request.post(info.baseUrl + `/admin/reports/expenses/${expense.id}/confirm`, { headers: { RequestVerificationToken: token }, data: { rowVersion: expense.rowVersion } }); assert.ok(confirmedResponse.ok());
    await page.goto(info.baseUrl + '/admin/reports/expenses'); await page.waitForFunction(() => document.querySelector('[data-management-report]')?.getAttribute('aria-busy') === 'false');
    await page.locator('.mr-table tbody tr').filter({ hasText: 'Chi phí kiểm tra thanh toán' }).locator('a[href*="payExpenseId"]').click(); await loaded();
    await page.waitForSelector('[data-money-dialog]:visible'); assert.equal(await page.locator('[data-money-form] [name=mode]').inputValue(), 'expense');
    assert.equal(await page.locator('[data-money-form] [name=amount]').inputValue(), '34'); await page.locator('[data-money-form] button[type=submit]').click(); await saved('[data-money-dialog]');
    assert.match(await page.locator('[data-table]').innerText(), /Chi phí kiểm tra thanh toán/);
    await go('debts');
    // Real supplier settlement through the page, including partial amount and source version.
    const debtRow = page.locator('[data-table] tbody tr').filter({ hasText: 'Nhà máy gạo Đồng Tháp' });
    await debtRow.locator('[data-pay]').click(); await page.waitForSelector('[data-money-dialog]:visible');
    const moneyForm = page.locator('[data-money-form]'); await moneyForm.locator('[name=fund]').selectOption('bank');
    await moneyForm.locator('[name=amount]').fill('1000000'); await moneyForm.locator('button[type=submit]').click();
    await saved('[data-money-dialog]'); assert.match(await debtRow.innerText(), /17\.500\.000/);
    await debtRow.locator('[data-due]').click(); await page.locator('[data-action-form] [name=date]').fill(info.day);
    await page.locator('[data-action-form] button[type=submit]').click(); await saved('[data-action-dialog]');
    assert.match(await debtRow.innerText(), /Chưa quá hạn/);
    await go('cashflow'); await page.locator('[data-money-new]').click(); await page.waitForSelector('[data-money-dialog]:visible');
    await moneyForm.locator('[name=mode]').selectOption('in'); await moneyForm.locator('[name=amount]').fill('12345'); await moneyForm.locator('[name=name]').fill('Thu đối chiếu trình duyệt');
    await moneyForm.locator('button[type=submit]').click(); await saved('[data-money-dialog]');
    const cashRow = page.locator('[data-table] tbody tr').filter({ hasText: 'Thu đối chiếu trình duyệt' });
    assert.match(await cashRow.innerText(), /12\.345/);
    await cashRow.locator('[data-reverse]').click(); await page.locator('[data-action-form] [name=reason]').fill('Tiền thử đã hoàn'); await page.locator('[data-action-form] button[type=submit]').click();
    await saved('[data-action-dialog]'); assert.equal(await page.locator('[data-table] tbody tr').filter({ hasText: 'Thu đối chiếu trình duyệt' }).count(), 2);
    await page.locator('[name=fund]').first().selectOption('cash'); await page.locator('[data-filters] button[type=submit]').click(); await loaded();
    await page.reload(); await loaded(); assert.equal(await page.locator('[data-filters] [name=fund]').inputValue(), 'cash');
    // Opening balance conflict stays reviewable inside the modal.
    await page.locator('[data-opening]').click(); await page.locator('[data-opening-form] [name=amount]').fill('0'); await page.locator('[data-opening-form] [name=note]').fill('Kiểm thử số dư đã tồn tại');
    await page.locator('[data-opening-form] button[type=submit]').click(); await page.waitForSelector('[data-opening-dialog] [data-form-error]:visible');
    await page.locator('[data-opening-dialog] [data-close]').first().click();
    // Failed requests hide stale results; retry restores the report.
    await page.route('**/admin/reports/operations/cashflow/data?**', route => route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ message: 'Báo cáo đang bận' }) }));
    await page.locator('[data-refresh]').first().click(); await page.waitForSelector('[data-error]:visible'); assert.equal(await page.locator('[data-results]').isVisible(), false);
    await page.unroute('**/admin/reports/operations/cashflow/data?**'); await page.locator('[data-error] [data-refresh]').click(); await loaded();
    await page.setViewportSize({ width: 390, height: 844 });
    for (const section of ['cashflow', 'inventory', 'debts']) { await go(section); assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'Mobile overflow: ' + section); await page.screenshot({ path: path.join(output, section + '-mobile.png'), fullPage: true }); }
    await page.locator('.mr-actions .layout-menu-toggle').click(); await page.waitForFunction(() => document.documentElement.classList.contains('layout-menu-expanded'));
    await page.locator('.layout-overlay').click({ position: { x: 380, y: 100 } }); await page.waitForFunction(() => !document.documentElement.classList.contains('layout-menu-expanded'));
    await page.setViewportSize({ width: 1480, height: 1080 }); await go('cashflow');
    await page.evaluate(() => document.documentElement.classList.add('dark-style')); await page.waitForFunction(() => document.querySelector('[data-operations-report]').dataset.reportTheme === 'dark'); await page.screenshot({ path: path.join(output, 'cashflow-dark.png'), fullPage: true });
    await page.locator('[data-filters] [name=fromDate]').fill('2001-01-01'); await page.locator('[data-filters] [name=toDate]').fill('2001-01-02'); await page.locator('[data-filters] button[type=submit]').click(); await loaded();
    assert.match(await page.locator('[data-table]').innerText(), /Không có dữ liệu/);
    assert.deepEqual(errors, []);
    console.log('PASS: three live reports, charts and CSV, partial supplier payment, due date, cash receipt/reversal, opening conflict, filters, error/retry, mobile, dark and empty data.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
