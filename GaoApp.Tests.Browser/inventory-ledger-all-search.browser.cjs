'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const mobile = require('./inventory-lookup-mobile-review.cjs');
(async () => {
    const info = JSON.parse(await new Promise(resolve => { let input = ''; process.stdin.on('data', c => input += c); process.stdin.on('end', () => resolve(input)); }));
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const proof = { cases: [], existingChromeWindowsPreserved: true }, errors = [];
    try {
        const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, reducedMotion: 'reduce', serviceWorkers: 'block' });
        await mobile.protect(context, info.baseUrl);
        const page = await context.newPage(); page.on('pageerror', error => errors.push(error.message));
        await page.goto(info.baseUrl + '/admin/account/login');
        await page.locator('[name=UserName]').fill(info.user); await page.locator('[name=Password]').fill(info.password);
        if (await page.locator('[name=SelectedTerminalId]').count()) await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([page.waitForURL(url => !url.pathname.endsWith('/login')), page.locator('button[type=submit]').click()]);
        async function ready() { await page.locator('#ledgerResultsPanel[aria-busy=false]').waitFor(); }
        async function search(keyword) {
            const received = page.waitForResponse(response => {
                const url = new URL(response.url());
                return url.pathname === '/admin/inventory-ledger/data' && url.searchParams.get('keyword') === keyword && url.searchParams.get('searchScope') === 'all';
            });
            received.catch(() => {}); // Keep an action error from being masked during browser cleanup.
            const start = Date.now();
            await page.locator('#ledgerKeyword').fill(keyword); await page.locator('#ledgerKeyword').press('Enter');
            const response = await received; assert.equal(response.status(), 200);
            const payload = await response.json(); await ready();
            const ms = Date.now() - start; assert.ok(ms < 20000, `Native All '${keyword}' reached the phone timeout: ${ms}ms`);
            return { payload, ms };
        }
        for (const width of [1440, 390]) {
            await page.setViewportSize({ width, height: 900 });
            await page.goto(info.baseUrl + '/admin/inventory-ledger'); await ready();
            const changed = page.waitForResponse(response => new URL(response.url()).pathname === '/admin/inventory-ledger/data'
                && new URL(response.url()).searchParams.get('searchScope') === 'all');
            changed.catch(() => {});
            await page.locator('#ledgerSearchScope').selectOption('all'); await changed; await ready();
            const { payload, ms } = await search('sua tuoi');
            assert.equal(payload.totalItems, 46); assert.equal(payload.summary.increaseItems, 24); assert.equal(payload.summary.decreaseItems, 22);
            assert.equal(payload.canViewCost, false); assert.equal(payload.items.length, 20);
            assert.equal(await page.locator(width < 768 ? '#ledgerMobileList .ledger-mobile-card' : '#ledgerDesktopBody .ledger-row').count(), 20);
            await page.screenshot({ fullPage: true, animations: 'disabled', path: path.join(info.output, `all-search-${width}-matches.png`) });
            const firstIds = payload.items.map(item => item.transactionId);
            const next = page.waitForResponse(response => {
                const url = new URL(response.url());
                return url.pathname === '/admin/inventory-ledger/data' && url.searchParams.get('page') === '2';
            });
            next.catch(() => {});
            await page.locator('#ledgerPagination [data-page="2"]').first().click();
            const secondResponse = await next; assert.equal(secondResponse.status(), 200);
            const second = await secondResponse.json(); await ready();
            assert.equal(second.totalItems, 46); assert.equal(second.summaryIncluded, false);
            assert.ok(second.items.every(item => !firstIds.includes(item.transactionId)));
            const noResult = await search('no-such-unique-ledger-value'); assert.equal(noResult.payload.totalItems, 0);
            assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false);
            proof.cases.push({ width, searchMs: ms, emptyMs: noResult.ms, resultCount: 46, nextPageHasNoDuplicates: true, noOverflow: true });
            await page.screenshot({ fullPage: true, animations: 'disabled', path: path.join(info.output, `all-search-${width}-empty.png`) });
        }
        assert.deepEqual(errors, []); proof.passed = true; proof.pageErrors = errors;
        fs.writeFileSync(path.join(info.output, 'browser-review.json'), JSON.stringify(proof, null, 2));
        await context.close(); console.log(JSON.stringify(proof));
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
