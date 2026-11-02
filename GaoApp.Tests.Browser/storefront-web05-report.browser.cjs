'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
(async () => {
  const info = JSON.parse(await new Promise(resolve => { let data = ''; process.stdin.on('data', x => data += x); process.stdin.on('end', () => resolve(data)); }));
  fs.mkdirSync(info.output, { recursive: true }); const errors = [], results = []; const browser = await chromium.launch({ channel: 'chrome', headless: true });
  const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, timezoneId: 'Asia/Bangkok', extraHTTPHeaders: { 'X-Web05-User': String(info.userId) }, acceptDownloads: true }); const page = await context.newPage();
  page.on('pageerror', error => errors.push(error.message));
  await context.route('**/*', route => new URL(route.request().url()).origin === new URL(info.baseUrl).origin ? route.continue() : route.abort('blockedbyclient'));
  try {
    const api = page.waitForResponse(r => new URL(r.url()).pathname === '/admin/reports/operations/cashflow/data');
    await page.goto(info.baseUrl + '/admin/reports/cashflow?fromDate=' + info.date + '&toDate=' + info.date); const response = await api; assert.equal(response.status(), 200); const data = await response.json();
    assert.equal(data.items[0].amountText, '9.007.199.254.740.993'); assert.equal(data.bankReceiptReviews[0].amountText, '9.007.199.254.740.993');
    await page.waitForFunction(() => document.querySelector('[data-operations-report]').getAttribute('aria-busy') === 'false');
    const text = await page.locator('[data-operations-report]').innerText(); assert.ok(text.includes('9.007.199.254.740.993')); assert.ok(!text.includes('9.007.199.254.740.992')); assert.ok(text.includes('ngày ngân hàng'));
    const review = await page.locator('[data-bank-receipt-reviews]').innerText(); assert.ok(review.includes('9.007.199.254.740.993')); assert.ok(review.includes('Chưa xác minh')); results.push({ name: 'actual report DOM preserves exact large verified and review amounts and day-only label', passed: true });
    await page.screenshot({ path: path.join(info.output, 'actual-production-report-large-money.png'), fullPage: true });
    const pending = page.waitForEvent('download'); await page.locator('[data-export]').click(); const file = await pending; await file.saveAs(path.join(info.output, 'actual-report.csv'));
    const csv = fs.readFileSync(path.join(info.output, 'actual-report.csv'), 'utf8'); assert.ok(csv.includes('9007199254740993')); assert.ok(!csv.includes('9007199254740992')); assert.ok(csv.includes('ngày ngân hàng')); results.push({ name: 'real report CSV download preserves all integer digits and time precision', passed: true });
    assert.deepEqual(errors, []);
  } catch (error) { results.push({ name: 'report browser', passed: false, error: error.stack }); console.error(error.stack); process.exitCode = 1; await page.screenshot({ path: path.join(info.output, 'failure.png'), fullPage: true }).catch(() => {}); }
  finally { fs.writeFileSync(path.join(info.output, 'browser-results.json'), JSON.stringify({ results, errors, browserVersion: browser.version(), mockAuthentication: 'fixture user with real same-store permission checks; no admin bootstrap' }, null, 2)); await context.close(); await browser.close(); }
})().catch(error => { console.error(error.stack); process.exitCode = 1; });
