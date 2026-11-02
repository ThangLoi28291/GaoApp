'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');

(async () => {
    const info = JSON.parse(await new Promise(resolve => {
        let text = ''; process.stdin.on('data', value => text += value); process.stdin.on('end', () => resolve(text));
    }));
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    try {
        fs.mkdirSync(info.evidenceRoot, { recursive: true });
        const buyer = await browser.newContext({ viewport: { width: 390, height: 844 } });
        const page = await buyer.newPage();
        const errors = []; page.on('pageerror', error => errors.push(error.message));
        await page.clock.install({ time: new Date() });
        const active = await page.goto(info.baseUrl + info.link);
        assert.equal(active.status(), 200);
        assert.match(active.headers()['cache-control'], /no-store/);
        assert.equal(active.headers()['referrer-policy'], 'no-referrer');
        assert.equal(await page.locator('#BuyerName').inputValue(), 'PRIVATE-BUYER-ISSUE4');
        await page.locator('#BuyerName').fill('BROWSER-UPDATED-ISSUE4');
        await page.locator('#BuyerAddress').fill('Browser test address');
        await Promise.all([page.waitForNavigation(), page.locator('button[type="submit"]').click()]);
        assert.equal(await page.locator('#BuyerName').inputValue(), 'BROWSER-UPDATED-ISSUE4');
        await page.screenshot({ path: path.join(info.evidenceRoot, 'active-mobile.png') });
        await page.goto(info.baseUrl + '/health/live');
        await page.goBack();
        await page.locator('#BuyerName').waitFor();
        assert.equal(await page.locator('#BuyerName').inputValue(), 'BROWSER-UPDATED-ISSUE4');
        // Move only the browser clock; the public view must clear even while left open.
        await page.clock.fastForward(3 * 60 * 60 * 1000);
        assert.equal(await page.locator('input').count(), 0);
        assert.doesNotMatch(await page.content(), /BROWSER-UPDATED-ISSUE4|012345678901|private-issue4@example.test/);
        await page.screenshot({ path: path.join(info.evidenceRoot, 'cleared-after-deadline.png') });
        await page.clock.setSystemTime(new Date());
        const employee = await browser.newContext();
        const admin = await employee.newPage();
        await admin.goto(info.baseUrl + '/admin/account/login');
        await admin.locator('[name="UserName"]').fill(info.user);
        await admin.locator('[name="Password"]').fill(info.password);
        await admin.locator('[name="SelectedTerminalId"]').selectOption(String(info.terminalId));
        await Promise.all([admin.waitForNavigation(), admin.locator('button[type="submit"]').click()]);
        // Stub only physical printing; use the real receipt Razor, QR renderer and replacement form.
        await admin.route('**/printing/pos.printing.js*', async route => {
            const response = await route.fetch();
            await route.fulfill({ response, body: await response.text() + '\nPosPrinting.sendReceipt = async () => ({ mode: "browser" });' });
        });
        await admin.goto(info.baseUrl + `/admin/pos/receipt/${info.orderId}`);
        await admin.waitForFunction(() => typeof window.GaoReceiptPrint?.setInvoiceBuyerLink === 'function' &&
            !document.getElementById('printReceipt').disabled);
        admin.once('dialog', dialog => dialog.accept());
        const replacedResponse = admin.waitForResponse(response => response.url().endsWith('/buyer-link/replace') && response.request().method() === 'POST');
        await admin.locator('#replaceInvoiceBuyerLink button').click();
        const replaced = await replacedResponse;
        assert.equal(replaced.status(), 200);
        const replacement = await replaced.json();
        await admin.waitForFunction(() => document.getElementById('replaceInvoiceBuyerLinkStatus').textContent.includes('Đã thu hồi QR cũ'));
        assert.equal((await buyer.request.get(info.baseUrl + info.link)).status(), 404);
        const newPage = await buyer.newPage();
        await newPage.goto(replacement.url);
        assert.equal(await newPage.locator('#BuyerName').inputValue(), 'BROWSER-UPDATED-ISSUE4');
        // A page restored from a frozen document clears and reloads, instead of showing old form values.
        await newPage.evaluate(() => window.dispatchEvent(new PageTransitionEvent('pagehide', { persisted: true })));
        assert.equal(await newPage.locator('input').count(), 0);
        await newPage.evaluate(() => window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true })));
        await newPage.locator('#BuyerName').waitFor();
        assert.equal(await newPage.locator('#BuyerName').inputValue(), 'BROWSER-UPDATED-ISSUE4');
        assert.deepEqual(errors, []);
        const result = { passed: true, checks: ['mobile form submit', 'cache and referrer headers', 'back navigation refresh',
            'open page expiry clears PII', 'authenticated replacement revokes old URL', 'new URL retains buyer draft', 'bfcache restoration reload'] };
        fs.writeFileSync(path.join(info.evidenceRoot, 'browser-verification.json'), JSON.stringify(result, null, 2));
        console.log(JSON.stringify(result));
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
