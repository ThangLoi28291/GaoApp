const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require('playwright');
(async () => {
    const info = JSON.parse(await new Promise(resolve => { let s = ''; process.stdin.on('data', x => s += x); process.stdin.on('end', () => resolve(s)); }));
    fs.mkdirSync('TestResults/ledger-search', { recursive: true });
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    try {
        const page = await browser.newPage({ viewport: { width: 1600, height: 1100 } });
        await page.addInitScript(() => { const timer = window.setTimeout; window.setTimeout = (fn, ms, ...args) => timer(fn, window.testShortTimeout && ms === 20000 ? 800 : ms, ...args); });
        await page.goto(info.baseUrl + '/admin/account/login');
        await page.locator('[name=UserName]').fill(info.user); await page.locator('[name=Password]').fill(info.password);
        if (await page.locator('[name=SelectedTerminalId]').count()) await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([page.waitForURL(u => !u.pathname.endsWith('/login')), page.locator('#loginForm button[type=submit]').click()]);
        await page.goto(info.baseUrl + '/admin/inventory-ledger');
        const input = page.locator('#ledgerKeyword');
        async function search(text) {
            const response = page.waitForResponse(r => r.url().includes('/inventory-ledger/data?') && new URL(r.url()).searchParams.get('keyword') === text && r.ok());
            await input.fill(text); await input.press('Enter');
            const result = await (await response).json();
            await page.waitForFunction(() => document.getElementById('ledgerResultsPanel').getAttribute('aria-busy') === 'false');
            return result;
        }
        assert.equal(await page.locator('#ledgerSearchScope').inputValue(), 'product');
        assert.equal((await search('sua tuoi')).totalItems, 45);
        assert.equal(await page.locator('#ledgerDesktopBody tr').count(), 20);
        assert.equal(await page.locator('.ledger-table th').filter({ hasText: /^Kho$/ }).count(), 0);
        assert.equal(await page.locator('#ledgerDesktopBody tr').first().locator('td').count(), 6);
        await page.screenshot({ path: 'TestResults/ledger-search/desktop.png', fullPage: true });
        assert.equal((await search('8938505974194')).totalItems, 45);
        await page.locator('#ledgerSearchScope').selectOption('note');
        assert.equal((await search('doi soat')).totalItems, 1);
        await page.locator('#ledgerSearchScope').selectOption('all');
        assert.equal((await search('sua tuoi')).totalItems, 46);
        await page.locator('#ledgerSearchScope').selectOption('reference');
        assert.equal((await search('NOTE-ONLY')).totalItems, 1);
        await page.locator('#ledgerSearchScope').selectOption('product');
        assert.equal((await search('no-such-value')).totalItems, 0);
        await page.route('**/inventory-ledger/data?**', async route => {
            const key = new URL(route.request().url()).searchParams.get('keyword');
            if (key === 'slow-old') { await new Promise(resolve => setTimeout(resolve, 1500)); await route.fulfill({ status: 500, body: '{}' }).catch(() => {}); }
            else if (key === 'timeout-case') { await new Promise(resolve => setTimeout(resolve, 1800)); await route.abort().catch(() => {}); }
            else await route.continue();
        });
        await input.fill('slow-old');
        await Promise.all([page.waitForRequest(r => r.url().includes('keyword=slow-old')), input.press('Enter')]);
        await search('sua tuoi');
        await page.waitForTimeout(1600);
        assert.equal(await page.locator('#ledgerDesktopBody tr').count(), 20);
        await page.evaluate(() => window.testShortTimeout = true);
        await input.fill('timeout-case'); await input.press('Enter');
        await page.locator('#ledgerDesktopBody').getByText('Tìm kiếm mất nhiều thời gian.', { exact: false }).waitFor();
        await page.evaluate(() => window.testShortTimeout = false);
        await search('sua tuoi');
        await page.setViewportSize({ width: 390, height: 844 });
        await page.locator('.ledger-mobile-card').first().waitFor();
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1));
        await page.screenshot({ path: 'TestResults/ledger-search/mobile.png', fullPage: true });
        await page.locator('.ledger-mobile-card .js-ledger-quick').first().click();
        await page.locator('.ledger-quick-facts').getByText('Kho', { exact: true }).waitFor();
    } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exit(1); });
