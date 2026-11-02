'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const mobile = require('./inventory-lookup-mobile-review.cjs');
(async () => {
    const info = JSON.parse(await new Promise(resolve => { let input = ''; process.stdin.on('data', c => input += c); process.stdin.on('end', () => resolve(input)); }));
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const errors = [], proof = { cases: [], existingChromeWindowsUntouched: true };
    try {
        async function login(account, width) {
            const context = await browser.newContext({ viewport: { width, height: 900 }, serviceWorkers: 'block', reducedMotion: 'reduce' });
            await mobile.protect(context, info.baseUrl);
            const page = await context.newPage();
            page.on('pageerror', e => errors.push(e.message));
            await page.goto(info.baseUrl + '/admin/account/login');
            await page.locator('[name=UserName]').fill(account.user); await page.locator('[name=Password]').fill(account.password);
            if (await page.locator('[name=SelectedTerminalId]').count()) await page.locator('[name=SelectedTerminalId]').selectOption(String(info.terminalId));
            await Promise.all([page.waitForURL(url => !url.pathname.endsWith('/login')), page.locator('button[type=submit]').click()]);
            return { context, page };
        }
        const admin = await login(info.manager, 1440), page = admin.page;
        let periodRequests = 0;
        page.on('request', request => { if (new URL(request.url()).pathname === '/admin/inventory-ledger/period') periodRequests++; });
        await page.goto(info.baseUrl + '/admin/inventory-ledger');
        await page.locator('#ledgerResultsPanel[aria-busy=false]').waitFor();
        await mobile.settle(page);
        assert.equal(periodRequests, 0, 'Desktop must not make summary requests');
        assert.equal(await page.locator('[data-ledger-period]').isVisible(), false);
        assert.equal(await page.locator('.ledger-source-link:visible').count(), 0);
        const desktopBefore = await page.screenshot({ fullPage: true, animations: 'disabled', path: path.join(info.output, 'desktop-1440.png') });
        await page.evaluate(() => {
            window.__periodPanel = document.querySelector('[data-ledger-period]');
            window.__periodNext = window.__periodPanel.nextSibling;
            window.__periodPanel.remove();
        });
        const desktopWithoutPanel = await page.screenshot({ fullPage: true, animations: 'disabled' });
        assert.ok(desktopBefore.equals(desktopWithoutPanel), 'Hidden phone summary must not change desktop pixels');
        await page.evaluate(() => window.__periodNext.parentNode.insertBefore(window.__periodPanel, window.__periodNext));
        proof.desktopHiddenPanelPixelIdentical = true;
        const searchIdentity = await page.locator('#ledgerKeyword').evaluateHandle(node => node);
        for (const width of [320, 390, 430]) {
            await page.setViewportSize({ width, height: 844 });
            await page.locator('[data-ledger-period][aria-busy=false] .ledger-period__metric').first().waitFor();
            await mobile.settle(page);
            const overflow = await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1);
            assert.equal(overflow, false, `Phone ${width} overflow`);
            assert.equal(await page.locator('#ledgerKeyword').evaluate((node, original) => node === original, searchIdentity), true);
            assert.equal(await page.locator('.ledger-period__metric').count(), 4);
            await page.screenshot({ fullPage: true, animations: 'disabled', path: path.join(info.output, `phone-${width}.png`) });
            proof.cases.push({ width, noOverflow: true, nativeFormIdentity: true, metrics: 4 });
        }
        await page.setViewportSize({ width: 390, height: 844 });
        await page.evaluate(() => {
            document.querySelector('#ledgerFromDate').value = '2026-01-10';
            document.querySelector('#ledgerToDate').value = '2026-01-10';
            document.querySelector('#ledgerToDate').dispatchEvent(new Event('change', { bubbles: true }));
        });
        await page.waitForFunction(() => document.querySelector('.ledger-period__caption')?.textContent.includes('2026-01-10'));
        await page.locator('#ledgerPeriodStock').selectOption(`${info.warehouseId}:${info.variantId}`);
        await page.waitForFunction(() => [...document.querySelectorAll('.ledger-period__metric strong')].map(n => n.textContent).join(',') === '107,10,3,114');
        await page.screenshot({ fullPage: true, animations: 'disabled', path: path.join(info.output, 'phone-period-vietnam-date.png') });
        const sourceCard = page.locator(`#ledgerMobileList [data-transaction-id="${info.transactionId}"]`);
        await sourceCard.locator('.ledger-source-link').waitFor();
        const sourceResponse = await admin.context.request.get(info.baseUrl + await sourceCard.locator('a').getAttribute('href'), { maxRedirects: 0 });
        assert.equal(sourceResponse.status(), 302);
        await sourceCard.locator('.js-ledger-quick').click();
        await page.locator('#ledgerQuickBody .ledger-source-link').waitFor();
        await page.screenshot({ animations: 'disabled', path: path.join(info.output, 'phone-source-quick-view.png') });
        await page.locator('#ledgerQuickViewModal .btn-close').click();
        await page.locator('#ledgerQuickViewModal').waitFor({ state: 'hidden' });
        // Failed totals never retain stale quantities; the explicit retry reads without writing.
        let fail = true;
        await admin.context.route('**/admin/inventory-ledger/period?**', route => fail ? route.fulfill({ status: 503, body: '{}' }) : route.continue());
        await page.locator('#ledgerPeriodStock').dispatchEvent('change');
        await page.locator('[data-period-result] button').waitFor();
        assert.equal(await page.locator('.ledger-period__metric').count(), 0);
        fail = false;
        await page.locator('[data-period-result] button').click();
        await page.locator('.ledger-period__metric').first().waitFor();
        proof.failedSummaryClearsStaleNumbersAndRetryWorks = true;
        await page.setViewportSize({ width: 1440, height: 900 });
        assert.equal(await page.locator('[data-ledger-period]').isVisible(), false);
        assert.equal(await page.locator('#ledgerKeyword').evaluate((node, original) => node === original, searchIdentity), true);
        const readOnly = await login(info.viewer, 390);
        await readOnly.page.goto(info.baseUrl + '/admin/inventory-ledger');
        await readOnly.page.locator('#ledgerResultsPanel[aria-busy=false]').waitFor();
        await readOnly.page.locator('.ledger-period__metric').first().waitFor();
        assert.equal(await readOnly.page.locator('.ledger-source-link').count(), 0);
        proof.viewerCanSeeTotalsButCannotOpenSource = true;
        assert.deepEqual(errors, []);
        proof.passed = true; proof.pageErrors = errors;
        fs.writeFileSync(path.join(info.output, 'browser-review.json'), JSON.stringify(proof, null, 2));
        await readOnly.context.close(); await admin.context.close();
        console.log(JSON.stringify(proof));
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
