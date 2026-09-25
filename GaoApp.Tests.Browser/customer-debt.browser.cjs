'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require('playwright');
(async () => {
    const info = JSON.parse(await new Promise(resolve => { let s = ''; process.stdin.on('data', x => s += x); process.stdin.on('end', () => resolve(s)); }));
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
    const errors = [];
    page.on('pageerror', e => errors.push(e.message));
    page.setDefaultTimeout(20000);
    fs.mkdirSync('TestResults/customer-debt', { recursive: true });
    try {
        await page.goto(info.baseUrl + '/admin/account/login');
        await page.locator('[name=UserName]').fill(info.user);
        await page.locator('[name=Password]').fill(info.password);
        await page.locator('select[name=SelectedTerminalId]').selectOption(String(info.terminalId));
        await Promise.all([page.waitForURL(u => !u.pathname.endsWith('/login')), page.locator('#loginForm button[type=submit]').click()]);
        await page.goto(info.baseUrl + '/admin/pos');
        await page.waitForFunction(() => window.posApp?.state.business.currentDraft?.lines?.length && window.PosOffline?.status().ready);
        await page.locator('#btnOpenPayment').click();
        await page.locator('#paymentModal.show').waitFor();
        assert.equal(await page.locator('#btnCreditPayment').isVisible(), false, 'Walk-in must not see credit');
        const assigned = await page.evaluate(async id => {
            const response = await fetch('/admin/pos/cart/current/customer/' + id, { method: 'POST',
                headers: { 'Content-Type': 'application/json', RequestVerificationToken: document.querySelector('[name=__RequestVerificationToken]').value },
                body: JSON.stringify({ repriceExistingLines: false }) });
            return response.ok;
        }, info.customerId);
        assert.ok(assigned);
        await page.reload();
        await page.waitForFunction(() => window.posApp?.state.business.currentDraft?.customerCanBuyOnCredit && window.PosOffline?.status().ready);
        await page.locator('#btnOpenPayment').click();
        await page.locator('#btnCreditPayment').click();
        assert.equal(await page.locator('#posCreditPanel select').count(), 0);
        assert.match(await page.locator('#creditCustomerName').innerText(), /Khách công nợ/);
        assert.equal(await page.locator('#creditDueDate').count(), 0);
        await page.screenshot({ path: 'TestResults/customer-debt/payment.png', fullPage: true });
        const posted = page.waitForResponse(r => r.url().includes('/finalize-credit') && r.request().method() === 'POST');
        await page.locator('#btnFinalizeFromPaymentModal').click();
        const response = await posted;
        assert.equal(response.status(), 200, await response.text());
        await page.goto(info.baseUrl + '/admin/customer-debt?customerId=' + info.customerId);
        await page.locator('#debtAmount').fill('10');
        const collected = page.waitForResponse(r => r.url().includes('/customer-debt/collect'));
        await page.locator('#debtSubmit').click();
        assert.equal((await collected).status(), 200);
        await page.waitForLoadState('networkidle');
        await page.locator('[data-debt-tab=receipts]').click();
        await page.locator('[data-debt-panel=receipts]').getByText('PTCN-000001', { exact: true }).waitFor();
        await page.screenshot({ path: 'TestResults/customer-debt/ledger.png', fullPage: true });
        await page.locator('[data-debt-tab=journal]').click();
        await page.screenshot({ path: 'TestResults/customer-debt/journal.png', fullPage: true });
        await page.locator('[data-debt-tab=orders]').click();
        await page.screenshot({ path: 'TestResults/customer-debt/orders.png', fullPage: true });
        await page.emulateMedia({ media: 'print' });
        assert.equal(await page.locator('[data-debt-panel=journal]').isVisible(), true);
        assert.equal(await page.locator('#debtCollectForm').isVisible(), false);
        await page.emulateMedia({ media: 'screen' });
        await page.setViewportSize({ width: 390, height: 844 });
        await page.screenshot({ path: 'TestResults/customer-debt/mobile.png', fullPage: true });
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), true);
        await page.setViewportSize({ width: 1440, height: 1000 });
        await page.goto(info.baseUrl + '/admin/customer-debt');
        await page.locator('.debt-name').first().waitFor();
        await page.screenshot({ path: 'TestResults/customer-debt/customers.png', fullPage: true });
        assert.deepEqual(errors, []);
        console.log('PASS: credit hidden without customer, inherited POS customer, credit checkout, cash collection, desktop/mobile rendering.');
    } catch (error) {
        await page.screenshot({ path: 'TestResults/customer-debt/failure.png', fullPage: true });
        console.error('Page errors:', errors);
        throw error;
    } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
